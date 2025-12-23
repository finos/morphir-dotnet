#!/usr/bin/env bash
# Script to create GitHub Project board for Morphir Application Architect Skill
#
# PREREQUISITES:
# Your GitHub token needs the 'project' scope.
# Add it at: https://github.com/settings/tokens
# Then run: gh auth refresh -s project
#
set -euo pipefail

echo "Creating GitHub Project board..."

# Get the organization/repository owner ID
OWNER_ID=$(gh api graphql -f query='
  query {
    repository(owner: "finos", name: "morphir-dotnet") {
      owner {
        id
      }
    }
  }
' --jq '.data.repository.owner.id')

echo "Owner ID: $OWNER_ID"

# Create the project
PROJECT_DATA=$(gh api graphql -f query='
  mutation($ownerId: ID!) {
    createProjectV2(input: {
      ownerId: $ownerId
      title: "Morphir Application Architect Skill Implementation"
    }) {
      projectV2 {
        id
        number
        url
      }
    }
  }
' -f ownerId="$OWNER_ID")

PROJECT_ID=$(echo "$PROJECT_DATA" | jq -r '.data.createProjectV2.projectV2.id')
PROJECT_URL=$(echo "$PROJECT_DATA" | jq -r '.data.createProjectV2.projectV2.url')

echo "Project created: $PROJECT_URL"
echo "Project ID: $PROJECT_ID"

# Add all issues to the project (Epic #314 and tasks #315-341)
echo "Adding issues to project..."

for issue_num in {314..341}; do
  echo "Adding issue #$issue_num..."

  # Get issue node ID
  ISSUE_ID=$(gh api graphql -f query='
    query($owner: String!, $repo: String!, $number: Int!) {
      repository(owner: $owner, name: $repo) {
        issue(number: $number) {
          id
        }
      }
    }
  ' -f owner="finos" -f repo="morphir-dotnet" -F number="$issue_num" --jq '.data.repository.issue.id')

  # Add issue to project
  gh api graphql -f query='
    mutation($projectId: ID!, $contentId: ID!) {
      addProjectV2ItemById(input: {
        projectId: $projectId
        contentId: $contentId
      }) {
        item {
          id
        }
      }
    }
  ' -f projectId="$PROJECT_ID" -f contentId="$ISSUE_ID" > /dev/null

  echo "  ✓ Added issue #$issue_num"
done

echo ""
echo "✅ Project board created successfully!"
echo "URL: $PROJECT_URL"
echo ""
echo "Next steps:"
echo "1. Visit the project board"
echo "2. Configure views (Board, Table, Roadmap)"
echo "3. Add custom fields if needed (e.g., Phase, Effort)"
echo "4. Set up automation rules"
