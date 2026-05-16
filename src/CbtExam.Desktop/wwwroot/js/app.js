/**
 * JAMB CBT Mock System - Student Portal Script
 * Handles real-time data, authentication, and UI states
 */

const API_BASE = '/api';

// --- Initialization ---
document.addEventListener('DOMContentLoaded', () => {
    updateDynamicYear();
    
    // Check if we are on the selection page
    if (document.getElementById('examList')) {
        initializeSelectionPage();
    }
    
    console.log('JAMB CBT Portal Initialized');
});

// --- UI Utilities ---
function updateDynamicYear() {
    const yearElements = document.querySelectorAll('#currentYear');
    const year = new Date().getFullYear();
    yearElements.forEach(el => el.textContent = year);
}

function togglePasswordVisibility() {
    const passwordInput = document.getElementById('password');
    const eyeIcon = document.getElementById('eyeIcon');
    
    if (passwordInput.type === 'password') {
        passwordInput.type = 'text';
        eyeIcon.innerHTML = `<path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/>`;
    } else {
        passwordInput.type = 'password';
        eyeIcon.innerHTML = `<path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>`;
    }
}

/**
 * Modern Toast Notification System
 * @param {string} title 
 * @param {string} message 
 * @param {string} type - 'success', 'error', 'info'
 */
function showToast(title, message, type = 'info') {
    const container = document.getElementById('toastContainer');
    if (!container) return;

    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    
    const icon = type === 'success' 
        ? '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg>'
        : type === 'error'
        ? '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>'
        : '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>';

    toast.innerHTML = `
        <div class="toast-icon">${icon}</div>
        <div class="toast-content">
            <div class="toast-title">${title}</div>
            <div class="toast-message">${message}</div>
        </div>
    `;

    container.appendChild(toast);

    // Auto remove after 5 seconds
    setTimeout(() => {
        toast.style.animation = 'toastFadeOut 0.5s ease forwards';
        setTimeout(() => toast.remove(), 500);
    }, 5000);
}

// --- Authentication ---
async function handleLogin(event) {
    event.preventDefault();
    
    const studentId = document.getElementById('username').value.trim();
    const password = document.getElementById('password').value.trim();
    const btn = document.getElementById('submitBtn');
    
    if (!studentId || !password) {
        showToast('Required', 'Please enter your student ID and password.', 'error');
        return;
    }

    btn.disabled = true;
    btn.innerHTML = '<div class="spinner"></div><span>Authenticating...</span>';

    try {
        // Use the dedicated student login endpoint (singular Student)
        const response = await fetch(`${API_BASE}/Student/login`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ studentId, password })
        });

        if (response.ok) {
            const user = await response.json();
            localStorage.setItem('studentName', user.fullName);
            localStorage.setItem('studentId', user.studentId);
            
            showToast('Success', `Welcome back, ${user.fullName.split(' ')[0]}!`, 'success');
            
            setTimeout(() => {
                window.location.href = 'selection.html';
            }, 1000);
        } else {
            const err = await response.json();
            showToast('Login Failed', err.error || 'Invalid credentials or account inactive.', 'error');
            resetLoginButton(btn);
        }
    } catch (error) {
        console.error('Login error:', error);
        showToast('Connection Error', 'Unable to reach the server. Please try again later.', 'error');
        resetLoginButton(btn);
    }
}

function resetLoginButton(btn) {
    btn.disabled = false;
    btn.innerHTML = '<span>Sign In</span><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></svg>';
}

// --- Selection Page Logic ---
async function initializeSelectionPage() {
    const studentName = localStorage.getItem('studentName') || 'Candidate';
    document.getElementById('userName').textContent = studentName;
    document.getElementById('userAvatar').textContent = studentName.charAt(0).toUpperCase();
    
    await fetchAndRenderExams();
}

async function fetchAndRenderExams() {
    const listContainer = document.getElementById('examList');
    
    try {
        const response = await fetch(`${API_BASE}/Exams`);
        if (!response.ok) throw new Error('API Error');
        
        const exams = await response.json();
        
        if (exams.length === 0) {
            listContainer.innerHTML = '<div class="empty-state">No examinations are currently scheduled.</div>';
            return;
        }

        listContainer.innerHTML = '';
        exams.forEach(exam => {
            const card = createExamCard(exam);
            listContainer.appendChild(card);
        });
    } catch (error) {
        console.error('Fetch exams error:', error);
        listContainer.innerHTML = '<div class="error-state">Failed to load examinations. Please check your connection.</div>';
        showToast('Error', 'Failed to synchronize available examinations.', 'error');
    }
}

function createExamCard(exam) {
    const div = document.createElement('div');
    div.className = 'exam-card-modern';
    div.onclick = () => {
        localStorage.setItem('selectedExamId', exam.id);
        localStorage.setItem('selectedExamTitle', exam.title);
        window.location.href = 'waiting.html';
    };

    div.innerHTML = `
        <div class="exam-card-info">
            <div class="exam-type-badge">ACTIVE</div>
            <h3>${exam.title}</h3>
            <div class="exam-meta-pills">
                <span class="meta-pill">${exam.questionCount || 0} Questions</span>
                <span class="meta-pill">${exam.durationMinutes} Minutes</span>
            </div>
        </div>
        <button class="action-btn">Start Session</button>
    `;
    
    return div;
}
