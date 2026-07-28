import sys

with open("src/ThisCafeteria.Web/Components/AgenticCommerce/ProcurementLab.razor", "r") as f:
    content = f.read()

# Add JSRuntime injection
content = content.replace(
    "@inject AuthenticationStateProvider AuthenticationStateProvider\n",
    "@inject AuthenticationStateProvider AuthenticationStateProvider\n@inject IJSRuntime JSRuntime\n"
)

# Add Action buttons section inside the job card
actions_html = """
                            <div class="job-card__actions mt-3">
                                @if (job.Status == "Open" && string.Equals(_walletAddress, job.ClientAddress, StringComparison.OrdinalIgnoreCase))
                                {
                                    <button class="btn btn-sm btn-primary" @onclick="() => FundJob(job)">Fund Job</button>
                                }
                                @if (job.Status == "Funded" && string.Equals(_walletAddress, job.ProviderAddress, StringComparison.OrdinalIgnoreCase))
                                {
                                    <button class="btn btn-sm btn-success" @onclick="() => SubmitJob(job)">Submit Evidence</button>
                                }
                                @if (job.Status == "Submitted" && string.Equals(_walletAddress, job.EvaluatorAddress, StringComparison.OrdinalIgnoreCase))
                                {
                                    <button class="btn btn-sm btn-info" @onclick="() => CompleteJob(job)">Complete Job</button>
                                    <button class="btn btn-sm btn-danger ms-2" @onclick="() => RejectJob(job)">Reject Job</button>
                                }
                            </div>
"""

content = content.replace(
    '</article>',
    actions_html + '\n                        </article>'
)

# Add "Simulate new Job" button to the header
header_btn = """
        <div class="procurement-lab__actions">
            @if (_walletAddress != null)
            {
                <button class="btn btn-outline-primary" @onclick="CreateTestJob">Simulate new Job</button>
            }
        </div>
"""

content = content.replace(
    '<div class="procurement-lab__source">',
    header_btn + '\n        <div class="procurement-lab__source">'
)

# Add the JS logic to @code
code_addition = """
    private IJSObjectReference? _agenticCommerce;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _agenticCommerce = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/agenticCommerce.js");
        }
    }

    private async Task CreateTestJob()
    {
        if (_agenticCommerce == null || _walletAddress == null) return;
        var chain = SelectedChainAccessor.SelectedChain;
        var contract = chain.Deployment.AgenticEscrow;
        // Provider = me, Evaluator = me, for testing
        await _agenticCommerce.InvokeVoidAsync("createJob", contract, _walletAddress, _walletAddress, "Test agentic commerce job");
        
        // Wait briefly for tx to process and worker to pick it up (in a real app we'd wait for the tx receipt or use polling)
        await Task.Delay(3000);
        await ReloadJobs();
    }

    private async Task FundJob(AgenticJobProjection job)
    {
        if (_agenticCommerce == null) return;
        var chain = SelectedChainAccessor.SelectedChain;
        var contract = chain.Deployment.AgenticEscrow;
        var token = chain.Deployment.PaymentToken;
        
        // We first need to set budget since it's 0 by default. Let's just set it to 10 for testing.
        await _agenticCommerce.InvokeVoidAsync("setBudget", contract, job.OnChainJobId, 10);
        await _agenticCommerce.InvokeVoidAsync("fundJob", contract, token, job.OnChainJobId, 10);
        
        await Task.Delay(3000);
        await ReloadJobs();
    }

    private async Task SubmitJob(AgenticJobProjection job)
    {
        if (_agenticCommerce == null) return;
        var chain = SelectedChainAccessor.SelectedChain;
        var contract = chain.Deployment.AgenticEscrow;
        await _agenticCommerce.InvokeVoidAsync("submitEvidence", contract, job.OnChainJobId, "IPFS_HASH_HERE");
        
        await Task.Delay(3000);
        await ReloadJobs();
    }

    private async Task CompleteJob(AgenticJobProjection job)
    {
        if (_agenticCommerce == null) return;
        var chain = SelectedChainAccessor.SelectedChain;
        var contract = chain.Deployment.AgenticEscrow;
        await _agenticCommerce.InvokeVoidAsync("completeJob", contract, job.OnChainJobId);
        
        await Task.Delay(3000);
        await ReloadJobs();
    }

    private async Task RejectJob(AgenticJobProjection job)
    {
        if (_agenticCommerce == null) return;
        var chain = SelectedChainAccessor.SelectedChain;
        var contract = chain.Deployment.AgenticEscrow;
        await _agenticCommerce.InvokeVoidAsync("rejectJob", contract, job.OnChainJobId);
        
        await Task.Delay(3000);
        await ReloadJobs();
    }

    private async Task ReloadJobs()
    {
        if (!string.IsNullOrWhiteSpace(_walletAddress)) Jobs = await JobService.GetJobsAsync(SelectedChainAccessor.SelectedChainKey, _walletAddress);
        StateHasChanged();
    }
"""

content = content.replace(
    'private bool _loading = true;',
    'private bool _loading = true;\n' + code_addition
)

with open("src/ThisCafeteria.Web/Components/AgenticCommerce/ProcurementLab.razor", "w") as f:
    f.write(content)

