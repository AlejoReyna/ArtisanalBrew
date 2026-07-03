using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Configuration;

namespace ThisCafeteria.Infrastructure.Services;

public sealed class ReceiptService(
    IS3StorageService blobStorage,
    IEmailSender emailSender,
    IOptions<AzureOptions> options,
    ILogger<ReceiptService> logger) : IReceiptService
{
    public async Task SendReceiptAsync(OrderDetails order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (string.IsNullOrWhiteSpace(order.OrderId))
        {
            throw new ArgumentException("OrderId is required.", nameof(order));
        }

        if (string.IsNullOrWhiteSpace(order.CustomerEmail))
        {
            throw new ArgumentException("CustomerEmail is required.", nameof(order));
        }

        var azureOptions = options.Value;
        if (string.IsNullOrWhiteSpace(azureOptions.Storage.BlobEndpoint))
        {
            throw new InvalidOperationException("Azure:Storage:BlobEndpoint is not configured.");
        }

        if (string.IsNullOrWhiteSpace(azureOptions.Communication.SenderAddress))
        {
            throw new InvalidOperationException("Azure:Communication:SenderAddress is not configured.");
        }

        var pdfBytes = GenerateReceiptPdf(order);
        var fileName = $"order-{order.OrderId}.pdf";

        var blobUri = await UploadReceiptToBlobAsync(pdfBytes, fileName, cancellationToken);
        await SendReceiptEmailAsync(order, pdfBytes, fileName, cancellationToken);

        logger.LogInformation(
            "Receipt generated, uploaded to {BlobUri}, and emailed to {Recipient}",
            blobUri,
            order.CustomerEmail);
    }

    private static byte[] GenerateReceiptPdf(OrderDetails order)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header()
                    .Column(column =>
                    {
                        column.Item().Text("Receipt")
                            .FontSize(28)
                            .SemiBold()
                            .FontColor(Colors.Blue.Darken2);

                        column.Item().Text($"Order #{order.OrderId}")
                            .FontSize(12)
                            .FontColor(Colors.Grey.Darken2);

                        column.Item().PaddingTop(8).LineHorizontal(1);
                    });

                page.Content()
                    .PaddingVertical(20)
                    .Column(column =>
                    {
                        column.Spacing(20);

                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Column(left =>
                            {
                                left.Item().Text("Billed To").SemiBold();
                                left.Item().Text(order.CustomerName);
                                left.Item().Text(order.CustomerEmail);
                            });

                            row.RelativeItem().AlignRight().Column(right =>
                            {
                                right.Item().Text("Purchase Date").SemiBold();
                                right.Item().Text(order.PurchaseDate.ToString("MMMM dd, yyyy"));
                            });
                        });

                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(4);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCell).Text("Item");
                                header.Cell().Element(HeaderCell).AlignRight().Text("Qty");
                                header.Cell().Element(HeaderCell).AlignRight().Text("Price");
                                header.Cell().Element(HeaderCell).AlignRight().Text("Total");
                            });

                            foreach (var item in order.Items)
                            {
                                table.Cell().Element(BodyCell).Text(item.Name);
                                table.Cell().Element(BodyCell).AlignRight().Text(item.Qty.ToString());
                                table.Cell().Element(BodyCell).AlignRight().Text(item.Price.ToString("C"));
                                table.Cell().Element(BodyCell).AlignRight().Text((item.Qty * item.Price).ToString("C"));
                            }
                        });

                        column.Item().AlignRight().Width(220).Column(summary =>
                        {
                            summary.Item().Row(row =>
                            {
                                row.RelativeItem().Text("Subtotal");
                                row.ConstantItem(90).AlignRight().Text(order.Subtotal.ToString("C"));
                            });

                            summary.Item().Row(row =>
                            {
                                row.RelativeItem().Text("Shipping");
                                row.ConstantItem(90).AlignRight().Text(order.Shipping.ToString("C"));
                            });

                            summary.Item().Row(row =>
                            {
                                row.RelativeItem().Text("Tax");
                                row.ConstantItem(90).AlignRight().Text(order.Tax.ToString("C"));
                            });

                            if (order.DiscountAmount > 0)
                            {
                                var couponLabel = string.IsNullOrWhiteSpace(order.CouponCode)
                                    ? "Coupon"
                                    : $"Coupon ({order.CouponCode})";
                                summary.Item().Row(row =>
                                {
                                    row.RelativeItem().Text(couponLabel);
                                    row.ConstantItem(90).AlignRight().Text($"-{order.DiscountAmount:C}");
                                });
                            }

                            summary.Item().PaddingTop(6).LineHorizontal(1);

                            summary.Item().PaddingTop(6).Row(row =>
                            {
                                row.RelativeItem().Text("Total").SemiBold();
                                row.ConstantItem(90).AlignRight().Text(order.Total.ToString("C")).SemiBold();
                            });
                        });
                    });

                page.Footer()
                    .AlignCenter()
                    .Text("Thank you for your purchase.")
                    .FontColor(Colors.Grey.Darken1);
            });
        }).GeneratePdf();

        static IContainer HeaderCell(IContainer container) =>
            container
                .Background(Colors.Blue.Darken2)
                .Padding(8)
                .DefaultTextStyle(x => x.FontColor(Colors.White).SemiBold());

        static IContainer BodyCell(IContainer container) =>
            container
                .BorderBottom(1)
                .BorderColor(Colors.Grey.Lighten2)
                .Padding(8);
    }

    private async Task<string> UploadReceiptToBlobAsync(
        byte[] pdfBytes,
        string fileName,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(pdfBytes);
        return await blobStorage.UploadAsync(stream, fileName, "application/pdf", cancellationToken);
    }

    private Task SendReceiptEmailAsync(
        OrderDetails order,
        byte[] pdfBytes,
        string fileName,
        CancellationToken cancellationToken)
    {
        var email = new OutboundEmail(
            order.CustomerEmail,
            $"Your receipt for order {order.OrderId}",
            $"""
            Hello {order.CustomerName},

            Thank you for your purchase. Your receipt for order {order.OrderId} is attached.

            Regards,
            This Cafeteria
            """,
            [new EmailAttachmentData(fileName, "application/pdf", pdfBytes)]);

        return emailSender.SendAsync(email, cancellationToken);
    }
}
