const express = require('express');
const fs = require('fs');
const path = require('path');
const cors = require('cors');
const https = require('https');

const app = express();
const PORT = process.env.PORT || 3000;
const ACCESS_CODE = process.env.ACCESS_CODE || 'JAMB2024';

// Use local questions directory or GitHub
const USE_GITHUB = process.env.USE_GITHUB === 'true';
const GITHUB_REPO = process.env.GITHUB_REPO || 'anointedthedeveloper/Q2';
const GITHUB_BRANCH = process.env.GITHUB_BRANCH || 'main';
const GITHUB_TOKEN = process.env.GITHUB_TOKEN;

const QUESTIONS_DIR = path.join(__dirname, '../questions');

app.use(cors());
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

// Get questions (from local or GitHub)
async function getQuestions(subject) {
    if (USE_GITHUB) {
        const content = await fetchFromGitHub(`questions/${subject}.json`);
        return JSON.parse(content);
    } else {
        const filePath = path.join(QUESTIONS_DIR, `${subject}.json`);
        const data = fs.readFileSync(filePath, 'utf8');
        return JSON.parse(data);
    }
}

// Get all subjects (from local or GitHub)
async function getSubjects() {
    if (USE_GITHUB) {
        const content = await fetchFromGitHub('questions');
        const files = JSON.parse(content);
        return files
            .filter(file => file.name.endsWith('.json') && file.size > 100)
            .map(file => file.name.replace('.json', ''));
    } else {
        const files = fs.readdirSync(QUESTIONS_DIR);
        return files
            .filter(file => file.endsWith('.json') && fs.statSync(path.join(QUESTIONS_DIR, file)).size > 100)
            .map(file => file.replace('.json', ''));
    }
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
app.get('/api/subjects', checkAccessCode, async (req, res) => {
    try {
        const subjects = await getSubjects();
        res.json({ subjects });
    } catch (error) {
        res.status(500).json({ error: 'Failed to read subjects', message: error.message });
    }
});

// Get questions for a specific subject
app.get('/api/questions/:subject', checkAccessCode, async (req, res) => {
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
app.get('/api/questions', checkAccessCode, async (req, res) => {
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
            subjects: 'GET /api/subjects (requires access code)',
            questionsBySubject: 'GET /api/questions/:subject (requires access code)',
            allQuestions: 'GET /api/questions?limit=N (requires access code)'
        },
        authentication: 'Provide access code via X-Access-Code header or access_code query parameter'
    });
});

app.listen(PORT, () => {
    console.log(`JAMB Questions Proxy running on port ${PORT}`);
    console.log(`Access Code: ${ACCESS_CODE}`);
    console.log(`Questions directory: ${QUESTIONS_DIR}`);
});
