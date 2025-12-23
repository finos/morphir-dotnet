# PowerShell script to add all Morphir Architect issues to Project #106
# Usage: .\scripts\add-to-project-106.ps1

$PROJECT_ID = "PVT_kwDOAhvSls4AqfzK"
$PROJECT_URL = "https://github.com/orgs/finos/projects/106"

Write-Host "Adding issues #314-341 to project: $PROJECT_URL" -ForegroundColor Cyan
Write-Host ""

$successCount = 0
$failCount = 0

foreach ($issueNum in 314..341) {
    Write-Host "Adding issue #$issueNum... " -NoNewline

    # Get issue node ID
    $issueQuery = @"
{
  "query": "query { repository(owner: \"finos\", name: \"morphir-dotnet\") { issue(number: $issueNum) { id } } }"
}
"@

    try {
        $issueResponse = gh api graphql -f query="query { repository(owner: \`"finos\`", name: \`"morphir-dotnet\`") { issue(number: $issueNum) { id } } }" | ConvertFrom-Json
        $issueId = $issueResponse.data.repository.issue.id

        if (-not $issueId) {
            Write-Host "❌ Issue not found" -ForegroundColor Red
            $failCount++
            continue
        }

        # Add to project
        $addMutation = "mutation { addProjectV2ItemById(input: { projectId: \`"$PROJECT_ID\`", contentId: \`"$issueId\`" }) { item { id } } }"
        $addResponse = gh api graphql -f query=$addMutation 2>&1

        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓" -ForegroundColor Green
            $successCount++
        }
        else {
            Write-Host "⚠️ (may already exist)" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
        $failCount++
    }
}

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Complete! Successfully added $successCount issues" -ForegroundColor Green
Write-Host "Failed/Skipped: $failCount issues" -ForegroundColor Yellow
Write-Host ""
Write-Host "View project board at: $PROJECT_URL" -ForegroundColor Cyan
