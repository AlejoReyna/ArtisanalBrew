using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Configuration;

namespace ThisCafeteria.Infrastructure.Services;

public sealed class ReceiptService(
    AmazonS3Client s3,
    AmazonSimpleEmailServiceV2Client ses,
    IOptions<AwsMessagingOptions> options,
    ILogger<ReceiptService> logger) : IReceiptService
{
    private const string ReceiptKeyPrefix = "receipts";

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

        var awsOptions = options.Value;
        if (string.IsNullOrWhiteSpace(awsOptions.S3BucketName))
        {
            throw new InvalidOperationException("AWS:S3BucketName is not configured.");
        }

        if (string.IsNullOrWhiteSpace(awsOptions.SesSenderEmail))
        {
            throw new InvalidOperationException("AWS:SesSenderEmail is not configured.");
        }

        var pdfBytes = GenerateReceiptPdf(order);
        var s3Key = $"{ReceiptKeyPrefix}/order-{order.OrderId}.pdf";
        var fileName = $"receipt-order-{order.OrderId}.pdf";

        await UploadReceiptToS3Async(pdfBytes, awsOptions.S3BucketName, s3Key, cancellationToken);
        await SendReceiptEmailAsync(order, pdfBytes, fileName, awsOptions.SesSenderEmail, cancellationToken);

        logger.LogInformation(
            "Receipt generated, uploaded to s3://{Bucket}/{Key}, and emailed to {Recipient}",
            awsOptions.S3BucketName,
            s3Key,
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
                                row.RelativeItem().Text("Tax");
                                row.ConstantItem(90).AlignRight().Text(order.Tax.ToString("C"));
                            });

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

    private async Task UploadReceiptToS3Async(
        byte[] pdfBytes,
        string bucketName,
        string s3Key,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(pdfBytes);

        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = s3Key,
            InputStream = stream,
            ContentType = "application/pdf",
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        };

        await s3.PutObjectAsync(request, cancellationToken);
    }

    private async Task SendReceiptEmailAsync(
        OrderDetails order,
        byte[] pdfBytes,
        string fileName,
        string senderEmail,
        CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(senderEmail));
        message.To.Add(MailboxAddress.Parse(order.CustomerEmail));
        message.Subject = $"Your receipt for order {order.OrderId}";

        var body = new BodyBuilder
        {
            TextBody = $"""
            Hello {order.CustomerName},

            Thank you for your purchase. Your receipt for order {order.OrderId} is attached.

            Regards,
            This Cafeteria
            """
        };

        body.Attachments.Add(fileName, pdfBytes, new ContentType("application", "pdf"));
        message.Body = body.ToMessageBody();

        await using var rawMessageStream = new MemoryStream();
        await message.WriteToAsync(rawMessageStream, cancellationToken);
        rawMessageStream.Position = 0;

        var request = new SendEmailRequest
        {
            FromEmailAddress = senderEmail,
            Destination = new Destination
            {
                ToAddresses = [order.CustomerEmail]
            },
            Content = new EmailContent
            {
                Raw = new RawMessage
                {
                    Data = rawMessageStream
                }
            }
        };

        await ses.SendEmailAsync(request, cancellationToken);
    }
}
