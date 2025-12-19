# Deployment Guide

This document explains how the documentation site is deployed to GitHub Pages.

## Automatic Deployment

The documentation site is automatically deployed to GitHub Pages via GitHub Actions when:

- Changes are pushed to the `main` branch in the `docs/` directory
- The workflow is manually triggered via `workflow_dispatch`

## Initial Setup

To enable GitHub Pages deployment:

1. Go to your repository settings on GitHub
2. Navigate to **Pages** in the left sidebar
3. Under **Source**, select **GitHub Actions**
4. The site will be automatically deployed after the first successful workflow run

## Site URL

Once deployed, the site will be available at:
- `https://finos.github.io/morphir-dotnet/`

## Manual Deployment

If you need to manually trigger a deployment:

1. Go to the **Actions** tab in your repository
2. Select the **Documentation** workflow
3. Click **Run workflow**
4. Select the branch (usually `main`)
5. Click **Run workflow**

## Troubleshooting

### Build Failures

If the build fails:

1. Check the GitHub Actions logs for errors
2. Ensure Hugo Extended is being used (required for Docsy theme)
3. Verify all theme dependencies are properly installed
4. Check that `go.mod` and `package.json` are up to date

### Site Not Updating

If the site isn't updating after a successful build:

1. Verify GitHub Pages is configured to use GitHub Actions as the source
2. Check that the deployment job completed successfully
3. Wait a few minutes for GitHub Pages to update (can take up to 10 minutes)
4. Clear your browser cache

### Local Build Issues

If you're having issues building locally:

1. Ensure you have Hugo Extended installed (not just Hugo)
2. Run `./setup.sh` to install all dependencies
3. Verify Go and Node.js are installed
4. Check that you're in the `docs/` directory when running commands




