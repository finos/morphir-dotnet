# Morphir .NET Documentation

This directory contains the Hugo-based documentation site for Morphir .NET.

## Prerequisites

- [Hugo Extended](https://gohugo.io/installation/) (v0.120.0 or later)
- Node.js and npm (for theme dependencies)

## Local Development

1. Install Hugo Extended:
   ```bash
   # macOS
   brew install hugo
   
   # Linux
   # Follow instructions at https://gohugo.io/installation/linux/
   ```

2. Install theme dependencies:
   ```bash
   cd docs
   npm install
   ```

3. Start the development server:
   ```bash
   hugo server
   ```

4. Open http://localhost:1313 in your browser

## Building

To build the static site:

```bash
hugo --minify
```

The output will be in the `public/` directory.

## Structure

- `content/` - Markdown content files
- `archetypes/` - Content templates
- `hugo.toml` - Hugo configuration
- `public/` - Generated static site (gitignored)

## Deployment

The site is automatically deployed to GitHub Pages via GitHub Actions when changes are pushed to the `main` branch.


