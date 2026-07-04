# JAMB Questions Proxy Server

A simple Node.js proxy server to serve JAMB exam questions with access code authentication.

## Features

- Serve JAMB questions by subject
- Access code authentication
- CORS enabled for cross-origin requests
- List all available subjects
- Get questions for specific subject or all questions

## Setup

1. Install dependencies:
```bash
npm install
```

2. Set your access code (optional):
```bash
# Windows
set ACCESS_CODE=your_secret_code

# Linux/Mac
export ACCESS_CODE=your_secret_code
```

Default access code is `JAMB2024`

3. Start the server:
```bash
npm start
```

Server will run on port 3000 by default. You can change it:
```bash
# Windows
set PORT=8080

# Linux/Mac
export PORT=8080
```

## API Endpoints

### GET /
Returns API information and available endpoints.

### GET /health
Health check endpoint.

### GET /api/subjects
Returns list of available subjects.
**Requires access code**

### GET /api/questions/:subject
Returns all questions for a specific subject.
**Requires access code**

Example: `GET /api/questions/Mathematics`

### GET /api/questions?limit=N
Returns all questions (optionally limited).
**Requires access code**

Example: `GET /api/questions?limit=100`

## Authentication

Provide your access code via:
- Header: `X-Access-Code: your_code`
- Query parameter: `?access_code=your_code`

## Example Usage

```bash
# Get subjects
curl -H "X-Access-Code: JAMB2024" http://localhost:3000/api/subjects

# Get Mathematics questions
curl -H "X-Access-Code: JAMB2024" http://localhost:3000/api/questions/Mathematics

# Get all questions (limited to 50)
curl -H "X-Access-Code: JAMB2024" "http://localhost:3000/api/questions?limit=50"
```

## Deployment

### Deploy to Vercel (Recommended)
1. Push this code to GitHub
2. Import project to Vercel
3. Add environment variables in Vercel dashboard:
   - `ACCESS_CODE` - Your secret access code
   - `USE_GITHUB` - Set to `true` to fetch questions from GitHub
   - `GITHUB_REPO` - e.g., `anointedthedeveloper/Q2`
   - `GITHUB_BRANCH` - e.g., `main`
   - `GITHUB_TOKEN` - Optional: Only needed to avoid GitHub API rate limits (60 requests/hour for unauthenticated requests)
4. Deploy

**Note:** For public repositories, `GITHUB_TOKEN` is optional. Without it, you're limited to 60 API requests per hour. If you expect higher traffic, create a token at https://github.com/settings/tokens with `public_repo` scope.

### Deploy to Render
1. Push this code to GitHub
2. Create a new Web Service on Render
3. Set environment variable `ACCESS_CODE` in Render dashboard
4. Deploy

### Deploy to Railway
1. Push this code to GitHub
2. Create a new project on Railway
3. Add environment variable `ACCESS_CODE`
4. Deploy

## Questions Directory

The server expects questions to be in JSON format in the `../questions` directory relative to this file. Each subject should have its own JSON file named `SubjectName.json`.

## Security

- Always use a strong access code
- Don't commit the access code to version control
- Use environment variables for sensitive configuration
