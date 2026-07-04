const https = require('https');

const ACCESS_CODE = process.env.ACCESS_CODE || 'JAMB2024';
const GITHUB_REPO = process.env.GITHUB_REPO || 'anointedthedeveloper/Q2';
const GITHUB_BRANCH = process.env.GITHUB_BRANCH || 'main';
const GITHUB_TOKEN = process.env.GITHUB_TOKEN;

// Fetch file from GitHub
async function fetchFromGitHub(filePath) {
    const url = `https://api.github.com/repos/${GITHUB_REPO}/contents/${filePath}?ref=${GITHUB_BRANCH}`;
    
    return new Promise((resolve, reject) => {
        const options = {
            headers: {
                'User-Agent': 'JAMB-Questions-Proxy',
                ...(GITHUB_TOKEN && { 'Authorization': `token ${GITHUB_TOKEN}` })
            }
        };

        https.get(url, options, (res) => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => {
                if (res.statusCode === 200) {
                    try {
                        const response = JSON.parse(data);
                        if (response.content) {
                            const content = Buffer.from(response.content, 'base64').toString('utf-8');
                            resolve(content);
                        } else {
                            reject(new Error('No content in response'));
                        }
                    } catch (e) {
                        reject(new Error('Failed to parse GitHub response'));
                    }
                } else {
                    reject(new Error(`GitHub API returned ${res.statusCode}`));
                }
            });
        }).on('error', reject);
    });
}

// Get questions (from GitHub)
async function getQuestions(subject) {
    const content = await fetchFromGitHub(`questions/${subject}.json`);
    return JSON.parse(content);
}

// Get all subjects (from GitHub)
async function getSubjects() {
    const content = await fetchFromGitHub('questions');
    const files = JSON.parse(content);
    return files
        .filter(file => file.name.endsWith('.json') && file.size > 100)
        .map(file => file.name.replace('.json', ''));
}

// Check access code
function checkAccessCode(req) {
    const providedCode = req.headers['x-access-code'] || (req.query && req.query.access_code);
    return providedCode === ACCESS_CODE;
}

// Vercel serverless handler
export default async function handler(req, res) {
    // Enable CORS
    res.setHeader('Access-Control-Allow-Credentials', true);
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET,OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'X-Access-Code, Content-Type');

    if (req.method === 'OPTIONS') {
        res.status(200).end();
        return;
    }

    const url = new URL(req.url, `http://${req.headers.host}`);
    const path = url.pathname;

    try {
        // Root endpoint
        if (path === '/') {
            res.json({
                message: 'JAMB Questions Proxy API',
                version: '1.0.0',
                endpoints: {
                    health: 'GET /health',
                    subjects: 'GET /subjects (requires access code)',
                    questionsBySubject: 'GET /questions/:subject (requires access code)',
                    allQuestions: 'GET /questions?limit=N (requires access code)'
                },
                authentication: 'Provide access code via X-Access-Code header or access_code query parameter'
            });
            return;
        }

        // Health check
        if (path === '/health') {
            res.json({ status: 'ok', timestamp: new Date().toISOString() });
            return;
        }

        // Get subjects
        if (path === '/subjects') {
            if (!checkAccessCode(req)) {
                res.status(401).json({ error: 'Unauthorized', message: 'Invalid or missing access code' });
                return;
            }
            const subjects = await getSubjects();
            res.json({ subjects });
            return;
        }

        // Get questions for specific subject
        const questionsMatch = path.match(/^\/questions\/(.+)$/);
        if (questionsMatch) {
            if (!checkAccessCode(req)) {
                res.status(401).json({ error: 'Unauthorized', message: 'Invalid or missing access code' });
                return;
            }
            const subject = decodeURIComponent(questionsMatch[1]);
            const questions = await getQuestions(subject);
            res.json({ subject, count: questions.length, questions });
            return;
        }

        // Get all questions
        if (path === '/questions') {
            if (!checkAccessCode(req)) {
                res.status(401).json({ error: 'Unauthorized', message: 'Invalid or missing access code' });
                return;
            }
            const limit = parseInt(url.searchParams.get('limit')) || 0;
            const subjects = await getSubjects();
            let allQuestions = [];
            for (const subject of subjects) {
                const questions = await getQuestions(subject);
                allQuestions = allQuestions.concat(questions);
            }
            if (limit > 0) {
                allQuestions = allQuestions.slice(0, limit);
            }
            res.json({ count: allQuestions.length, questions: allQuestions });
            return;
        }

        // 404
        res.status(404).json({ error: 'Not Found' });
    } catch (error) {
        res.status(500).json({ error: 'Internal Server Error', message: error.message });
    }
}

export const config = {
    runtime: 'nodejs',
};
