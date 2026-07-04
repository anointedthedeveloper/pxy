const express = require('express');
const https = require('https');

const app = express();
const ACCESS_CODE = process.env.ACCESS_CODE || 'JAMB2024';
const USE_GITHUB = process.env.USE_GITHUB === 'true';
const GITHUB_REPO = process.env.GITHUB_REPO || 'anointedthedeveloper/Q2';
const GITHUB_BRANCH = process.env.GITHUB_BRANCH || 'main';
const GITHUB_TOKEN = process.env.GITHUB_TOKEN;

app.use(express.json());

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

// Middleware to check access code
const checkAccessCode = (req, res, next) => {
    const providedCode = req.headers['x-access-code'] || req.query.access_code;
    
    if (!providedCode || providedCode !== ACCESS_CODE) {
        return res.status(401).json({ 
            error: 'Unauthorized',
            message: 'Invalid or missing access code'
        });
    }
    next();
};

// Get list of available subjects
app.get('/subjects', checkAccessCode, async (req, res) => {
    try {
        const subjects = await getSubjects();
        res.json({ subjects });
    } catch (error) {
        res.status(500).json({ error: 'Failed to read subjects', message: error.message });
    }
});

// Get questions for a specific subject
app.get('/questions/:subject', checkAccessCode, async (req, res) => {
    try {
        const { subject } = req.params;
        const questions = await getQuestions(subject);
        
        res.json({ 
            subject,
            count: questions.length,
            questions
        });
    } catch (error) {
        res.status(500).json({ 
            error: 'Failed to load questions',
            message: error.message
        });
    }
});

// Get all questions (with optional limit)
app.get('/questions', checkAccessCode, async (req, res) => {
    try {
        const limit = parseInt(req.query.limit) || 0;
        const subjects = await getSubjects();
        
        let allQuestions = [];
        
        for (const subject of subjects) {
            const questions = await getQuestions(subject);
            allQuestions = allQuestions.concat(questions);
        }
        
        if (limit > 0) {
            allQuestions = allQuestions.slice(0, limit);
        }
        
        res.json({ 
            count: allQuestions.length,
            questions: allQuestions
        });
    } catch (error) {
        res.status(500).json({ 
            error: 'Failed to load questions',
            message: error.message
        });
    }
});

// Health check endpoint
app.get('/health', (req, res) => {
    res.json({ status: 'ok', timestamp: new Date().toISOString() });
});

// Root endpoint
app.get('/', (req, res) => {
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
});

module.exports = app;
