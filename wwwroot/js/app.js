// --- Dynamic API Base configuration ---
let API_BASE = '/api';

// Auto-detect and fall back to saved Server IP if opened via file:// protocol
if (window.location.protocol === 'file:') {
    const savedServerIp = localStorage.getItem('cbt_server_ip') || 'localhost';
    API_BASE = `http://${savedServerIp}:7031/api`;
}

// --- Utility Functions ---

function escapeHtml(unsafe) {
    if (typeof unsafe !== 'string') return unsafe;
    return unsafe
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

// Request fullscreen on exam page load for JAMB-like experience
// Note: Fullscreen requires user gesture, so this will fail silently
// Fullscreen will be triggered when user clicks "Start Exam" button
document.addEventListener('DOMContentLoaded', () => {
    // Removed auto-fullscreen on load - requires user gesture
});

// --- Device fingerprinting and heartbeat locking (wrapped in try-catch to prevent crashes in private modes) ---
let deviceId = 'NODE-UNKNOWN';
try {
    deviceId = localStorage.getItem('cbt_device_id');
    if (!deviceId) {
        deviceId = 'NODE-' + Math.random().toString(36).substring(2, 11).toUpperCase();
        localStorage.setItem('cbt_device_id', deviceId);
    }
} catch (e) {
    deviceId = 'NODE-' + Math.random().toString(36).substring(2, 11).toUpperCase();
}

function getBrowserAndOS() {
    const ua = navigator.userAgent;
    let browser = "Browser";

    if (ua.indexOf("Firefox") > -1) browser = "Firefox";
    else if (ua.indexOf("Chrome") > -1 && ua.indexOf("Edge") === -1 && ua.indexOf("Edg") === -1) browser = "Chrome";
    else if (ua.indexOf("Safari") > -1 && ua.indexOf("Chrome") === -1) browser = "Safari";
    else if (ua.indexOf("Edge") > -1 || ua.indexOf("Edg") > -1) browser = "Edge";
    else if (ua.indexOf("Trident") > -1 || ua.indexOf("MSIE") > -1) browser = "IE";

    return browser;
}

async function runDeviceHeartbeat() {
    // Device heartbeat is optional - don't fail if endpoint doesn't exist
    try {
        const studentExamId = localStorage.getItem('studentExamId');
        if (studentExamId) {
            // Use GET endpoint for heartbeat instead of POST to avoid validation issues
            await fetch(`${API_BASE}/Student/${studentExamId}/progress`);
        }
    } catch (e) {
        console.warn("Device heartbeat failed", e);
    }
}

// --- Clear all student session data (called on logout / exam exit / fresh visit) ---
function clearAllStudentData() {
    const keysToRemove = [
        'studentId', 'studentName', 'studentExamId', 'selectedExamId', 'selectedExamTitle',
        'examDuration', 'cachedQuestions', 'encryptedQuestions',
        'sessionId', 'selectedSessionId', 'selectedSessionCode', 'cbt_session_code', 'last_broadcast_msg'
    ];
    keysToRemove.forEach(k => localStorage.removeItem(k));
    Object.keys(localStorage).forEach(k => {
        if (k.startsWith('studentExamAnswers_') || k.startsWith('examStart_')) {
            localStorage.removeItem(k);
        }
    });
    // NOTE: lastExamResult is intentionally kept so results can display after logout redirect
}

// --- Logout Handler ---
function handleLogout() {
    if (confirm('Are you sure you want to logout?')) {
        clearAllStudentData();
        localStorage.removeItem('lastExamResult'); // Also clear results on logout
        window.location.href = '/login';
    }
}

// --- Smart User & Session Detection ---
function runSmartSessionDetection() {
    const currentPath = window.location.pathname;
    const isLoginPage = currentPath.endsWith('/login') || currentPath.endsWith('/index') || currentPath.endsWith('/') || currentPath === '';
    const hasActiveUser = localStorage.getItem('studentId');

    // Always wipe session data when landing on login page — ensures fresh start on revisit
    if (isLoginPage) {
        clearAllStudentData();
        return false;
    }

    if (!hasActiveUser) {
        if (window.location.protocol !== 'file:') {
            window.location.href = '/login';
            return true;
        }
    }
    return false;
}

// --- Initialization ---
document.addEventListener('DOMContentLoaded', () => {
    if (runSmartSessionDetection()) return;
    
    updateDynamicYear();
    initConnectionSettings();
    
    // Update login page stats if on login page
    if (document.getElementById('activeExams')) {
        updateLoginStats();
        loadSchoolBranding();
    }
    
    // Check if we are on the selection page
    if (document.getElementById('examList')) {
        initializeSelectionPage();
    }
    
    console.log('JAMB CBT Portal Initialized');
    
    // Start device heartbeat loop
    runDeviceHeartbeat();
    setInterval(runDeviceHeartbeat, 5000);

    // --- SignalR: instant broadcast receiver ---
    try {
        const hubUrl = API_BASE.replace('/api', '') + '/hubs/exam';
        const examHub = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect()
            .build();

        examHub.on('BroadcastMessage', (payload) => {
            const msg = payload?.message || payload;
            if (!msg) return;
            const lastBroadcast = localStorage.getItem('last_broadcast_msg');
            if (lastBroadcast !== msg) {
                localStorage.setItem('last_broadcast_msg', msg);
                if (typeof showToast === 'function')
                    showToast('Broadcast from Coordinator', msg, 'info');
            }
        });

        examHub.start().catch(() => { /* offline — heartbeat poll is fallback */ });
    } catch (e) { /* signalR not loaded (e.g. on non-exam pages) */ }
});

// --- Connection Settings for file:// Protocol ---
function initConnectionSettings() {
    const box = document.getElementById('connectionSettings');
    const input = document.getElementById('serverIpInput');
    if (!box || !input) return;

    if (window.location.protocol === 'file:') {
        box.style.display = 'block';
        const saved = localStorage.getItem('cbt_server_ip') || 'localhost';
        input.value = saved;
    }
}

function saveServerIp() {
    const input = document.getElementById('serverIpInput');
    if (!input) return;
    const ip = input.value.trim();
    if (ip) {
        localStorage.setItem('cbt_server_ip', ip);
        showToast('Connection Saved', `Connected to coordinator server at http://${ip}:7031`, 'success');
        setTimeout(() => {
            window.location.reload();
        }, 1000);
    }
}

// --- UI Utilities ---
function updateDynamicYear() {
    const yearElements = document.querySelectorAll('#currentYear');
    const year = new Date().getFullYear();
    yearElements.forEach(el => el.textContent = year);
}

async function updateLoginStats() {
    try {
        // Update exam year
        const examYearEl = document.getElementById('examYear');
        if (examYearEl) {
            examYearEl.textContent = new Date().getFullYear();
        }

        // Fetch active sessions count
        const activeExamsEl = document.getElementById('activeExams');
        if (activeExamsEl) {
            try {
                const response = await fetch(`${API_BASE}/Sessions`);
                if (response.ok) {
                    const sessions = await response.json();
                    const activeSessions = sessions.filter(s => s.isActive === true);
                    activeExamsEl.textContent = activeSessions.length;
                }
            } catch (e) {
                console.warn('Could not fetch active exams count:', e);
            }
        }

        // Fetch total questions count (this would need a new API endpoint)
        const totalQuestionsEl = document.getElementById('totalQuestions');
        if (totalQuestionsEl) {
            try {
                const response = await fetch(`${API_BASE}/Questions/count`);
                if (response.ok) {
                    const data = await response.json();
                    totalQuestionsEl.textContent = data.count || 0;
                }
            } catch (e) {
                console.warn('Could not fetch total questions count:', e);
                totalQuestionsEl.textContent = '0';
            }
        }
    } catch (e) {
        console.warn('Error updating login stats:', e);
    }
}

async function loadSchoolBranding() {
    try {
        console.log('Fetching school branding from:', `${API_BASE}/Config/branding`);
        const response = await fetch(`${API_BASE}/Config/branding`);
        console.log('Branding response status:', response.status);
        
        if (response.ok) {
            const data = await response.json();
            console.log('Branding data received:', data);
            
            // Load school logo
            const schoolLogoContainer = document.getElementById('schoolLogoContainer');
            const schoolLogoImg = document.getElementById('schoolLogo');
            
            if (data.schoolLogo && schoolLogoContainer && schoolLogoImg) {
                console.log('Setting school logo');
                schoolLogoImg.src = 'data:image/png;base64,' + data.schoolLogo;
                schoolLogoContainer.style.display = 'flex';
            }
            
            // Load school name
            if (data.systemName) {
                console.log('Setting school name:', data.systemName);
                const schoolTitle = document.getElementById('schoolTitle');
                const rightPanelTitle = document.querySelector('.right-panel h2');
                const tickerContent = document.getElementById('tickerContent');
                
                if (schoolTitle) {
                    schoolTitle.innerHTML = `${data.systemName}<br><span class="green-text">JAMB CBT Mock System</span>`;
                }
                if (rightPanelTitle) {
                    rightPanelTitle.textContent = `${data.systemName} JAMB CBT Mock System`;
                }
            } else {
                console.log('No system name in branding data');
            }
        } else {
            console.error('Failed to fetch branding:', response.status);
        }
    } catch (e) {
        console.error('Could not fetch school branding:', e);
    }
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

function showToast(title, message, type = 'info') {
    const container = document.getElementById('toastContainer');
    if (!container) return;

    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    
    const icons = {
        success: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg>',
        error:   '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>',
        info:    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M18 3a3 3 0 0 0-3 3v12a3 3 0 0 0 3 3 3 3 0 0 0 3-3 3 3 0 0 0-3-3H6a3 3 0 0 0-3 3 3 3 0 0 0 3 3 3 3 0 0 0 3-3V6a3 3 0 0 0-3-3 3 3 0 0 0-3 3 3 3 0 0 0 3 3h12a3 3 0 0 0 3-3 3 3 0 0 0-3-3z"></path></svg>',
        broadcast: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07A19.5 19.5 0 0 1 4.69 12 19.79 19.79 0 0 1 1.61 3.44a2 2 0 0 1 1.97-2.18h3a2 2 0 0 1 2 1.72c.127.96.361 1.903.7 2.81a2 2 0 0 1-.45 2.11L7.91 8.96a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.907.339 1.85.573 2.81.7A2 2 0 0 1 22 16.92z"/></svg>'
    };
    const iconHtml = title.toLowerCase().includes('broadcast') ? icons.broadcast : (icons[type] || icons.info);

    toast.innerHTML = `
        <div class="toast-icon">${iconHtml}</div>
        <div class="toast-content">
            <div class="toast-title">${escapeHtml(title)}</div>
            <div class="toast-message">${escapeHtml(message)}</div>
        </div>
    `;

    container.appendChild(toast);

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
    
    // Clear previous validation states
    clearValidationErrors();
    
    // Validate inputs
    let hasError = false;
    if (!studentId) {
        showInputError('username', 'Student ID is required');
        hasError = true;
    }
    
    // Normalize student ID (remove leading zeros for comparison)
    const normalizedStudentId = studentId.replace(/^0+/, '') || '0';
    
    if (!password) {
        showInputError('password', 'Password is required');
        hasError = true;
    }
    
    if (hasError) {
        shakeForm();
        return;
    }

    btn.disabled = true;
    btn.innerHTML = '<div class="spinner"></div><span>Authenticating...</span>';

    try {
        // Use the dedicated student login endpoint (singular Student)
        const response = await fetch(`${API_BASE}/Student/login`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ studentId: normalizedStudentId, password, deviceId })
        });

        if (response.ok) {
            const user = await response.json();
            localStorage.setItem('studentName', user.fullName);
            localStorage.setItem('studentId', user.studentId);
            
            showToast('Success', `Welcome back, ${user.fullName.split(' ')[0]}!`, 'success');
            
            setTimeout(() => {
                window.location.href = '/selection';
            }, 1000);
        } else if (response.status === 409) {
            const err = await response.json();
            showToast('Already Logged In', err.error || 'This account is active on another device. Ask the invigilator to reset your session.', 'error');
            resetLoginButton(btn);
            shakeForm();
        } else if (response.status === 401) {
            const err = await response.json();
            showToast('Login Failed', err.error || 'Invalid student ID or password. Please check your credentials.', 'error');
            resetLoginButton(btn);
            showInputError('username', 'Invalid student ID or password');
            shakeForm();
        } else {
            const err = await response.json();
            showToast('Login Failed', err.error || 'Unable to login. Your account may be inactive.', 'error');
            resetLoginButton(btn);
            shakeForm();
        }
    } catch (error) {
        console.error('Login error:', error);
        showToast('Connection Error', 'Unable to reach the server. Check your internet connection and try again.', 'error');
        resetLoginButton(btn);
        shakeForm();
    }
}

function showInputError(inputId, message) {
    const input = document.getElementById(inputId);
    const wrapper = input.closest('.input-wrapper');
    
    input.style.borderColor = '#ef4444';
    input.setAttribute('aria-invalid', 'true');
    
    // Remove existing error message if any
    const existingError = wrapper.querySelector('.error-message');
    if (existingError) existingError.remove();
    
    // Add error message
    const errorDiv = document.createElement('div');
    errorDiv.className = 'error-message';
    errorDiv.textContent = message;
    errorDiv.style.cssText = 'color: #ef4444; font-size: 12px; margin-top: 6px; font-weight: 500;';
    wrapper.after(errorDiv);
}

function clearValidationErrors() {
    const inputs = document.querySelectorAll('#loginForm input');
    inputs.forEach(input => {
        input.style.borderColor = '';
        input.setAttribute('aria-invalid', 'false');
    });
    
    const errorMessages = document.querySelectorAll('.error-message');
    errorMessages.forEach(msg => msg.remove());
}

function shakeForm() {
    const form = document.getElementById('loginForm');
    form.style.animation = 'shake 0.5s ease-in-out';
    setTimeout(() => {
        form.style.animation = '';
    }, 500);
}

function resetLoginButton(btn) {
    btn.disabled = false;
    btn.innerHTML = '<span>Sign In</span><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></svg>';
}

// Help Modal Functions
function showHelpModal(event) {
    if (event) event.preventDefault();
    const modal = document.getElementById('helpModal');
    modal.style.display = 'flex';
    document.body.style.overflow = 'hidden';
}

function closeHelpModal() {
    const modal = document.getElementById('helpModal');
    modal.style.display = 'none';
    document.body.style.overflow = '';
}

// Close modal on overlay click
document.addEventListener('DOMContentLoaded', () => {
    const modal = document.getElementById('helpModal');
    if (modal) {
        modal.addEventListener('click', (e) => {
            if (e.target === modal) {
                closeHelpModal();
            }
        });
    }
    
    // Close modal on Escape key
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            closeHelpModal();
        }
    });
});

// Add real-time validation on input
document.addEventListener('DOMContentLoaded', () => {
    const usernameInput = document.getElementById('username');
    const passwordInput = document.getElementById('password');
    
    // Browser compatibility check
    checkBrowserCompatibility();
    
    if (usernameInput) {
        usernameInput.addEventListener('input', () => {
            clearValidationErrors();
        });
    }
    
    if (passwordInput) {
        passwordInput.addEventListener('input', () => {
            clearValidationErrors();
        });
    }
    
    // Focus username field on page load
    if (usernameInput) {
        usernameInput.focus();
    }
    
    // Initialize network status
    updateNetworkStatus();
    window.addEventListener('online', updateNetworkStatus);
    window.addEventListener('offline', updateNetworkStatus);
});

function checkBrowserCompatibility() {
    const ua = navigator.userAgent;
    let isCompatible = true;
    let message = '';
    
    // Check for IE
    if (ua.indexOf('MSIE') > -1 || ua.indexOf('Trident') > -1) {
        isCompatible = false;
        message = 'Internet Explorer is not supported. Please use Chrome, Firefox, or Edge.';
    }
    
    // Check for very old browsers
    const isOldSafari = /^((?!chrome|android).)*safari/i.test(ua) && 
                       (parseInt(ua.match(/Version\/(\d+)/)?.[1] || '0') < 14);
    if (isOldSafari) {
        isCompatible = false;
        message = 'Please update your Safari browser to version 14 or higher.';
    }
    
    if (!isCompatible) {
        showToast('Browser Not Supported', message, 'error');
    }
}

function updateNetworkStatus() {
    const networkStatus = document.getElementById('networkStatus');
    const connectionText = document.getElementById('connectionText');
    if (!networkStatus) return;
    
    const wifiIcon = document.getElementById('wifiIcon');
    const noWifiIcon = document.getElementById('noWifiIcon');
    const lanIcon = document.getElementById('lanIcon');
    
    if (!navigator.onLine) {
        // Offline - show no wifi icon
        networkStatus.classList.remove('online');
        networkStatus.classList.add('offline');
        networkStatus.title = 'Offline - No internet connection';
        
        if (wifiIcon) wifiIcon.style.display = 'none';
        if (noWifiIcon) noWifiIcon.style.display = 'block';
        if (lanIcon) lanIcon.style.display = 'none';
        if (connectionText) connectionText.style.display = 'none';
    } else {
        // Online - determine connection type
        networkStatus.classList.remove('offline');
        networkStatus.classList.add('online');
        
        // Try to detect connection type using Network Information API
        let connectionType = 'wifi'; // default to wifi
        
        if (navigator.connection && navigator.connection.type) {
            connectionType = navigator.connection.type;
        }
        
        // Show appropriate icon based on connection type
        if (connectionType === 'ethernet' || connectionType === 'lan') {
            networkStatus.title = 'Online - Connected via LAN';
            if (wifiIcon) wifiIcon.style.display = 'none';
            if (noWifiIcon) noWifiIcon.style.display = 'none';
            if (lanIcon) lanIcon.style.display = 'block';
            if (connectionText) {
                connectionText.style.display = 'block';
                connectionText.textContent = 'Connected via LAN';
            }
        } else {
            networkStatus.title = 'Online - Connected to server';
            if (wifiIcon) wifiIcon.style.display = 'block';
            if (noWifiIcon) noWifiIcon.style.display = 'none';
            if (lanIcon) lanIcon.style.display = 'none';
            if (connectionText) {
                connectionText.style.display = 'block';
                connectionText.textContent = 'Connected to server';
            }
        }
    }
}

// --- Selection Page Logic ---
async function initializeSelectionPage() {
    const studentName = localStorage.getItem('studentName') || 'Candidate';
    document.getElementById('userName').textContent = studentName;
    document.getElementById('userAvatar').textContent = studentName.charAt(0).toUpperCase();
    
    await fetchAndRenderExams();
    
    // Poll for new exams/sessions every 4 seconds to instantly detect started exam sessions!
    setInterval(fetchAndRenderExams, 4000);
}

async function fetchAndRenderExams() {
    const listContainer = document.getElementById('examList');
    const studentId = localStorage.getItem('studentId');
    
    try {
        const response = await fetch(`${API_BASE}/Sessions`);
        if (!response.ok) throw new Error('API Error');
        
        const sessions = await response.json();
        
        // Filter for active sessions only
        const activeSessions = sessions.filter(s => s.isActive === true);
        
        if (activeSessions.length === 0) {
            listContainer.innerHTML = '<div class="empty-state">No examination sessions are currently active.</div>';
            return;
        }

        // Fetch student's completed exams if logged in
        let completedExamIds = new Set();
        if (studentId) {
            try {
                const progressRes = await fetch(`${API_BASE}/Student/${studentId}/completed-exams`);
                if (progressRes.ok) {
                    const completedData = await progressRes.json();
                    completedExamIds = new Set(completedData.map(e => e.sessionId));
                }
            } catch (e) {
                console.warn('Could not fetch completed exams:', e);
            }
        }

        listContainer.innerHTML = '';
        activeSessions.forEach(session => {
            const isCompleted = completedExamIds.has(session.id);
            const card = createSessionCard(session, isCompleted);
            listContainer.appendChild(card);
        });
    } catch (error) {
        console.error('Fetch sessions error:', error);
        listContainer.innerHTML = '<div class="error-state">Failed to load sessions. Please check your connection.</div>';
        showToast('Error', 'Failed to synchronize available sessions.', 'error');
    }
}

function createSessionCard(session, isCompleted = false) {
    const div = document.createElement('div');
    div.className = 'exam-card-modern' + (isCompleted ? ' locked' : '');
    
    if (isCompleted) {
        div.onclick = () => {
            showToast('Already Completed', 'You have already taken this exam. View your results instead.', 'info');
        };
    } else {
        div.onclick = () => {
            localStorage.setItem('selectedSessionId', session.id);
            localStorage.setItem('selectedSessionCode', session.sessionCode);
            localStorage.setItem('selectedExamId', session.examId);
            localStorage.setItem('selectedExamTitle', session.displayName || session.examTitle);
            window.location.href = '/waiting';
        };
    }

    div.innerHTML = `
        <div class="exam-type-badge ${isCompleted ? 'gray' : ''}">${isCompleted ? 'COMPLETED' : 'ACTIVE'}</div>
        <h3>${escapeHtml(session.displayName || session.examTitle)}</h3>
        <div class="exam-meta-pills">
            <span class="meta-pill">Code: ${escapeHtml(session.sessionCode)}</span>
            <span class="meta-pill">${session.studentCount || 0} Students</span>
            <span class="meta-pill">${session.isStarted ? 'Started' : 'Waiting'}</span>
        </div>
        <button class="action-btn" ${isCompleted ? 'disabled' : ''}>${isCompleted ? 'View Results' : 'Join Session'}</button>
        ${isCompleted ? '<p class="exam-lock-reason">You have already completed this examination.</p>' : ''}
    `;
    
    return div;
}

// ── Candidate Examination Runner Engine ──

let questions = [];
let currentIndex = 0;
let studentExamId = null;
let studentId = null;
let studentName = null;
let examDuration = 60; // default 60 minutes
let timeRemaining = 3600;
let timerInterval = null;
let responses = {};
let flagged = {};
let examCompleted = false;
let heartbeatInterval = null;
let subjects = [];
let activeSubject = '';

// Add initialization checking for exam page
document.addEventListener('DOMContentLoaded', () => {
    if (document.getElementById('question-nav')) {
        initializeExamPage();
    }
});

async function initializeExamPage() {
    studentExamId = localStorage.getItem('studentExamId');
    studentId = localStorage.getItem('studentId') || '00000000';
    studentName = localStorage.getItem('studentName') || 'Candidate';
    examDuration = parseInt(localStorage.getItem('examDuration')) || 60;
    const examTitle = localStorage.getItem('selectedExamTitle') || 'Examination';

    console.log('[Exam Page] Initializing with:', { studentExamId, examTitle, examDuration, studentId, studentName });

    // Validate studentExamId - redirect to waiting room if missing
    if (!studentExamId) {
        showToast('Error', 'Missing exam session ID. Redirecting to selection page...', 'error');
        setTimeout(() => {
            window.location.href = '/selection';
        }, 2000);
        return;
    }

    // Set exam title in header and browser tab
    const examTitleEl = document.getElementById('exam-title');
    if (examTitleEl) examTitleEl.textContent = examTitle;
    document.title = examTitle + ' | CBT Portal';

    // Set user info in header
    document.getElementById('user-name').textContent = studentName;
    document.getElementById('user-id').textContent = `ID: ${studentId}`;
    const avatar = document.getElementById('user-avatar-initial');
    if (avatar) avatar.textContent = studentName.charAt(0).toUpperCase();

    // Set completion modal message
    const completionMsg = document.getElementById('completion-msg');
    if (completionMsg) completionMsg.textContent = `Your ${examTitle} has been submitted successfully.`;
    const completionExamName = document.getElementById('completion-exam-name');
    if (completionExamName) completionExamName.textContent = examTitle;

    // Load cached questions
    const cached = localStorage.getItem('cachedQuestions');
    if (cached) {
        try {
            const parsed = JSON.parse(cached);
            // Handle both old array format and new object format in cache
            questions = Array.isArray(parsed) ? parsed : (parsed.questions || []);
            console.log('[Exam Page] Loaded', questions.length, 'questions from cache');
        } catch (e) {
            console.error('Failed to parse cached questions:', e);
            questions = [];
        }
    } else {
        console.warn('[Exam Page] No cached questions found, fetching from API');
        // Fallback fetch
        try {
            const res = await fetch(`${API_BASE}/Student/${studentExamId}/questions`);
            if (res.ok) {
                const data = await res.json();
                // Handle both old array format and new object format
                questions = Array.isArray(data) ? data : (data.questions || []);
                console.log('[Exam Page] Fetched', questions.length, 'questions from API');
                
                // Save exam duration if provided
                if (data.durationMinutes) {
                    localStorage.setItem('examDuration', data.durationMinutes);
                }
                
                localStorage.setItem('cachedQuestions', JSON.stringify(questions));
            } else {
                showToast('Error', 'Could not load examination questions.', 'error');
                return;
            }
        } catch (err) {
            console.error(err);
            showToast('Error', 'Connection failure. Could not download questions.', 'error');
            return;
        }
    }

    if (questions.length === 0) {
        showToast('Empty Exam', 'There are no questions in this examination.', 'error');
        return;
    }

    // Load saved answers
    const savedAnswers = localStorage.getItem(`studentExamAnswers_${studentExamId}`);
    if (savedAnswers) {
        responses = JSON.parse(savedAnswers);
    } else {
        // Load initial progress from server in case of refresh or device switch
        try {
            const progressRes = await fetch(`${API_BASE}/Student/${studentExamId}/progress`);
            if (progressRes.ok) {
                const prog = await progressRes.json();
                if (prog.answers) {
                    prog.answers.forEach(a => {
                        responses[a.questionId] = a.selectedAnswer;
                    });
                    localStorage.setItem(`studentExamAnswers_${studentExamId}`, JSON.stringify(responses));
                }
            }
        } catch (e) {
            console.warn("Could not load initial progress from API", e);
        }
    }

    // Initialize UI
    currentIndex = 0;
    initSubjectTabs();
    renderNavigator();
    renderQuestion(currentIndex);
    updateProgressRing();
    
    // Start countdown and backend updates
    startTimer();
    startHeartbeat();
    setupEventListeners();
}

function setupEventListeners() {
    // Keyboard shortcuts
    document.addEventListener('keydown', (e) => {
        if (examCompleted) return;
        
        // If calculator is open, do not trigger exam shortcuts
        const calcModal = document.getElementById('calculator-modal');
        if (calcModal && !calcModal.classList.contains('hidden')) {
            return;
        }

        const key = e.key.toUpperCase();
        if (key === 'A' || key === 'B' || key === 'C' || key === 'D') {
            const currentQ = questions[currentIndex];
            if (currentQ && currentQ.options) {
                const optIndex = key.charCodeAt(0) - 65; // 0, 1, 2, 3
                if (optIndex < currentQ.options.length) {
                    selectOption(currentQ.questionId, currentQ.options[optIndex]);
                }
            }
        } else if (key === 'N' || e.key === 'ArrowRight') {
            nextQuestion();
        } else if (key === 'P' || e.key === 'ArrowLeft') {
            prevQuestion();
        } else if (key === 'S') {
            confirmSubmit();
        }
    });

    // Window blur tab-switch reporting
    document.addEventListener('visibilitychange', () => {
        if (document.hidden && !examCompleted) {
            fetch(`${API_BASE}/Student/tabswitch`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ studentExamId: parseInt(studentExamId) })
            }).catch(err => console.error('Tab switch report error', err));
            
            showToast('Warning', 'Tab switching detected! This incident has been logged by the coordinator.', 'error');
        }
    });
}

function startTimer() {
    let examStart = localStorage.getItem(`examStart_${studentExamId}`);
    if (!examStart) {
        examStart = Date.now();
        localStorage.setItem(`examStart_${studentExamId}`, examStart);
    }
    
    const durationSeconds = examDuration * 60;
    
    const updateTimerDisplay = () => {
        const elapsedSeconds = Math.floor((Date.now() - parseInt(examStart)) / 1000);
        timeRemaining = durationSeconds - elapsedSeconds;
        
        if (timeRemaining <= 0) {
            timeRemaining = 0;
            document.getElementById('timer').textContent = "00:00:00";
            clearInterval(timerInterval);
            // Automatic submit on timeout
            submitExam(false);
            return;
        }
        
        const hrs = Math.floor(timeRemaining / 3600);
        const mins = Math.floor((timeRemaining % 3600) / 60);
        const secs = timeRemaining % 60;
        
        document.getElementById('timer').textContent = 
            `${hrs.toString().padStart(2, '0')}:${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
            
        // Alert visually in final 5 minutes
        if (timeRemaining < 300) {
            const timerBox = document.getElementById('timer-box');
            if (timerBox) timerBox.classList.add('warning');
        }
    };
    
    updateTimerDisplay();
    timerInterval = setInterval(updateTimerDisplay, 1000);
}

function startHeartbeat() {
    const sendHeartbeat = async () => {
        if (examCompleted) return;
        try {
            const activeDeviceId = localStorage.getItem('cbt_device_id') || 'NODE-UNKNOWN';
            const activeDeviceName = getBrowserAndOS();
            let batteryLevel = 100;
            try {
                if (navigator.getBattery) {
                    const battery = await navigator.getBattery();
                    batteryLevel = Math.round(battery.level * 100);
                }
            } catch (e) { }

            const response = await fetch(`${API_BASE}/Student/heartbeat`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    studentExamId: parseInt(studentExamId),
                    currentQuestion: currentIndex + 1,
                    batteryLevel: batteryLevel,
                    isOnline: true,
                    connectionState: "Excellent",
                    deviceName: activeDeviceName,
                    deviceId: activeDeviceId
                })
            });
            if (response.ok) {
                setNetworkStatus(true); // heartbeat success = we're back online
                const data = await response.json();
                if (data && data.broadcastMessage) {
                    const lastBroadcast = localStorage.getItem('last_broadcast_msg');
                    if (lastBroadcast !== data.broadcastMessage) {
                        localStorage.setItem('last_broadcast_msg', data.broadcastMessage);
                        showToast('Broadcast from Coordinator', data.broadcastMessage, 'info');
                    }
                }
            } else {
                setNetworkStatus(false);
            }
        } catch (e) {
            setNetworkStatus(false);
        }
    };
    
    sendHeartbeat();
    heartbeatInterval = setInterval(sendHeartbeat, 10000);
}

function initSubjectTabs() {
    const normalize = q => (q.subject && q.subject.trim()) ? q.subject.trim() : 'General';
    subjects = [...new Set(questions.map(normalize))];
    subjects.sort((a, b) => {
        const aL = a.toLowerCase(), bL = b.toLowerCase();
        if (aL.includes('english')) return -1;
        if (bL.includes('english')) return 1;
        if (aL.includes('math')) return -1;
        if (bL.includes('math')) return 1;
        return a.localeCompare(b);
    });
    activeSubject = subjects[0];
    renderSubjectTabs();
}

function renderSubjectTabs() {
    const contentBar    = document.getElementById('subject-content-bar');

    // Count questions per subject for the badges
    const subjectCounts = {};
    subjects.forEach(sub => {
        subjectCounts[sub] = questions.filter(q => ((q.subject && q.subject.trim()) ? q.subject.trim() : 'General') === sub).length;
    });

    // Content-area underline tabs (only one subject navigator)
    if (contentBar) {
        contentBar.innerHTML = '';
        subjects.forEach(sub => {
            const btn = document.createElement('button');
            btn.className = 'strip-tab' + (sub === activeSubject ? ' active' : '');
            btn.innerHTML = sub + '<span class="strip-tab-pill">' + subjectCounts[sub] + '</span>';
            btn.onclick = () => switchSubject(sub);
            contentBar.appendChild(btn);
        });
    }
}

function switchSubject(sub) {
    activeSubject = sub;
    renderSubjectTabs();
    
    // Jump to first question of this subject
    const subQuestions = getFilteredQuestions();
    const firstSubQ = subQuestions[0];
    if (firstSubQ) {
        const globalIndex = questions.findIndex(q => q.questionId === firstSubQ.questionId);
        if (globalIndex !== -1) {
            currentIndex = globalIndex;
            renderQuestion(currentIndex);
            renderNavigator();
        }
    }
}

function getFilteredQuestions() {
    return questions.filter(q => ((q.subject && q.subject.trim()) ? q.subject.trim() : 'General') === activeSubject);
}

function renderNavigator() {
    const grid = document.getElementById('question-nav');
    if (!grid) return;
    grid.innerHTML = "";

    const subQuestions = getFilteredQuestions();
    subQuestions.forEach((q, subIndex) => {
        const btn = document.createElement('button');
        btn.className = 'nav-btn';
        
        const qId = q.questionId;
        const globalIndex = questions.findIndex(x => x.questionId === qId);
        const isCurrent = globalIndex === currentIndex;
        const isAnswered = responses[qId] !== undefined;
        const isFlagged = flagged[qId] === true;

        if (isCurrent) btn.classList.add('current');
        if (isAnswered) btn.classList.add('answered');
        if (isFlagged) btn.classList.add('flagged');

        btn.textContent = subIndex + 1;
        btn.onclick = () => jumpToQuestion(globalIndex);
        grid.appendChild(btn);
    });

    // Update counts — support both old and new HTML IDs
    const totalCount = questions.length;
    const answeredCount = Object.keys(responses).length;
    ['total-count', 'sb-total'].forEach(id => { const el = document.getElementById(id); if (el) el.textContent = totalCount; });
    ['answered-count', 'sb-answered'].forEach(id => { const el = document.getElementById(id); if (el) el.textContent = answeredCount; });
}

function renderQuestion(index) {
    const q = questions[index];
    if (!q) return;

    // Auto-select subject tab if we navigated to a question belonging to another subject
    const sub = (q.subject && q.subject.trim()) ? q.subject.trim() : 'General';
    if (sub !== activeSubject) {
        activeSubject = sub;
        renderSubjectTabs();
    }

    const subQuestions = getFilteredQuestions();
    const subIndex = subQuestions.findIndex(x => x.questionId === q.questionId);
    const globalNum = index + 1;

    // Controls footer label
    const ctrlNum = document.getElementById('current-q-num');
    if (ctrlNum) ctrlNum.textContent = `Question ${subIndex + 1} of ${subQuestions.length}`;

    // New badge elements
    const numBadge = document.getElementById('q-num-badge');
    if (numBadge) numBadge.textContent = `Q ${subIndex + 1}`;

    const yearBadge = document.getElementById('q-year-badge');
    if (yearBadge) {
        if (q.year && q.year > 0) {
            yearBadge.textContent = q.year;
            yearBadge.style.display = '';
        } else {
            yearBadge.style.display = 'none';
        }
    }

    const subjBadge = document.getElementById('q-subj-badge');
    if (subjBadge) {
        subjBadge.textContent = sub;
        subjBadge.style.display = '';
    }

    // Use innerHTML so HTML tags like <u>, <b>, <i>, <sup>, <sub> render
    const qtEl = document.getElementById('question-text');
    if (qtEl) qtEl.innerHTML = q.text;

    // Section / passage
    const sectionWrap = document.getElementById('question-section');
    const sectionContent = document.getElementById('question-section-content');
    if (sectionWrap && sectionContent) {
        if (q.section && q.section.trim()) {
            // Hide sections containing "solution" during exam mode (only show in results)
            const sectionText = q.section.toLowerCase();
            const isSolution = sectionText.includes('solution');
            
            if (isSolution && !examCompleted) {
                sectionWrap.style.display = 'none';
            } else {
                sectionContent.innerHTML = q.section;
                sectionWrap.style.display = 'block';
            }
        } else {
            sectionWrap.style.display = 'none';
        }
    }

    // Image
    const imgWrap = document.getElementById('q-img-wrap');
    const imgEl = document.getElementById('question-image');
    if (imgWrap && imgEl) {
        if (q.imageUrl && q.imageUrl.trim()) {
            imgEl.src = q.imageUrl;
            imgWrap.style.display = 'block';
        } else {
            imgEl.src = '';
            imgWrap.style.display = 'none';
        }
    }

    // Flag button
    const flagBtn = document.getElementById('btn-flag');
    if (flagBtn) {
        if (flagged[q.questionId]) flagBtn.classList.add('flagged');
        else flagBtn.classList.remove('flagged');
    }

    renderOptions(q);
}

function renderOptions(question) {
    const list = document.getElementById('options-list');
    if (!list) return;
    list.innerHTML = "";
    
    const selectedAnswer = responses[question.questionId];
    
    question.options.forEach((optText, index) => {
        const letter = String.fromCharCode(65 + index);
        const isSelected = selectedAnswer === optText;
        
        const item = document.createElement('div');
        item.className = `opt option-item${isSelected ? ' selected' : ''}`;
        item.onclick = () => selectOption(question.questionId, optText);
        
        item.innerHTML = `
            <div class="opt-letter option-letter">${letter}</div>
            <div class="opt-text option-text">${escapeHtml(optText)}</div>
            <div class="opt-check">
              <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
            </div>
        `;

        // CRITICAL: actually append to the list
        list.appendChild(item);
    });
}

async function selectOption(questionId, optionText) {
    if (examCompleted) return;
    
    responses[questionId] = optionText;
    localStorage.setItem(`studentExamAnswers_${studentExamId}`, JSON.stringify(responses));
    
    // Refresh UIs
    const currentQ = questions[currentIndex];
    renderOptions(currentQ);
    renderNavigator();
    updateProgressRing();
    
    // Save progress to server; queue locally if offline
    const progressPayload = {
        studentExamId: parseInt(studentExamId),
        questionId: questionId,
        selectedAnswer: optionText
    };
    try {
        await fetch(`${API_BASE}/Student/progress`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(progressPayload)
        });
    } catch (e) {
        // Offline — queue for sync when reconnected
        _pendingProgressSync.push(progressPayload);
    }
}

async function clearCurrentAnswer() {
    if (examCompleted) return;
    const currentQ = questions[currentIndex];
    if (!currentQ) return;
    
    delete responses[currentQ.questionId];
    localStorage.setItem(`studentExamAnswers_${studentExamId}`, JSON.stringify(responses));
    
    renderOptions(currentQ);
    renderNavigator();
    updateProgressRing();
    
    try {
        await fetch(`${API_BASE}/Student/progress`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                studentExamId: parseInt(studentExamId),
                questionId: currentQ.questionId,
                selectedAnswer: ""
            })
        });
    } catch (e) {
        console.warn("Failed to update clear selection in API", e);
    }
}

function toggleFlagCurrent() {
    if (examCompleted) return;
    const currentQ = questions[currentIndex];
    if (!currentQ) return;

    flagged[currentQ.questionId] = !flagged[currentQ.questionId];
    renderQuestion(currentIndex);
    renderNavigator();
}

function jumpToQuestion(index) {
    if (index < 0 || index >= questions.length) return;
    currentIndex = index;
    renderQuestion(currentIndex);
    renderNavigator();
}

function nextQuestion() {
    const subQuestions = getFilteredQuestions();
    const currentQ = questions[currentIndex];
    const subIndex = subQuestions.findIndex(x => x.questionId === currentQ.questionId);
    
    // If current question is not in filtered list (shouldn't happen), fall back to global navigation
    if (subIndex === -1) {
        if (currentIndex < questions.length - 1) {
            jumpToQuestion(currentIndex + 1);
        }
        return;
    }
    
    if (subIndex < subQuestions.length - 1) {
        const nextQ = subQuestions[subIndex + 1];
        const globalIndex = questions.findIndex(x => x.questionId === nextQ.questionId);
        if (globalIndex !== -1) {
            jumpToQuestion(globalIndex);
        }
    }
}

function prevQuestion() {
    const subQuestions = getFilteredQuestions();
    const currentQ = questions[currentIndex];
    const subIndex = subQuestions.findIndex(x => x.questionId === currentQ.questionId);
    
    // If current question is not in filtered list (shouldn't happen), fall back to global navigation
    if (subIndex === -1) {
        if (currentIndex > 0) {
            jumpToQuestion(currentIndex - 1);
        }
        return;
    }
    
    if (subIndex > 0) {
        const prevQ = subQuestions[subIndex - 1];
        const globalIndex = questions.findIndex(x => x.questionId === prevQ.questionId);
        if (globalIndex !== -1) {
            jumpToQuestion(globalIndex);
        }
    }
}

function updateProgressRing() {
    const ring = document.querySelector('.progress-ring__circle');
    const pctSpan = document.getElementById('progress-pct');
    if (!ring) return;

    const total = questions.length;
    if (total === 0) return;

    const answered = Object.keys(responses).length;
    const pct = Math.round((answered / total) * 100);
    if (pctSpan) pctSpan.textContent = `${pct}%`;

    const r = parseFloat(ring.getAttribute('r') || '13');
    const circumference = r * 2 * Math.PI;
    ring.style.strokeDasharray = `${circumference} ${circumference}`;
    ring.style.strokeDashoffset = circumference - (pct / 100) * circumference;
}

// ── Simple Calculator ──

let calcExpression = "";
function toggleCalculator() {
    const modal = document.getElementById('calculator-modal');
    if (modal) {
        modal.classList.toggle('hidden');
        if (!modal.classList.contains('hidden')) {
            calcExpression = "";
            document.getElementById('calc-display').textContent = "0";
        }
    }
}

function pressCalc(val) {
    const display = document.getElementById('calc-display');
    if (!display) return;

    if (val === 'C') {
        calcExpression = "";
        display.textContent = "0";
    } else if (val === 'back') {
        calcExpression = calcExpression.slice(0, -1);
        display.textContent = calcExpression || "0";
    } else if (val === '=') {
        try {
            // Safe eval: only allow digits, operators, dot, parens
            if (!/^[0-9+\-*/.() ]+$/.test(calcExpression)) throw new Error('invalid');
            let result = Function('"use strict"; return (' + calcExpression + ')')();
            if (!isFinite(result)) { display.textContent = "Error"; calcExpression = ""; return; }
            result = Math.round(result * 1e10) / 1e10;
            display.textContent = result.toString();
            calcExpression = result.toString();
        } catch (e) {
            display.textContent = "Error";
            calcExpression = "";
        }
    } else {
        // Prevent double operators
        const ops = ['+', '-', '*', '/'];
        if (ops.includes(val) && ops.includes(calcExpression.slice(-1))) {
            calcExpression = calcExpression.slice(0, -1);
        }
        calcExpression += val;
        display.textContent = calcExpression;
    }
}

// ── Network Status Tracker ──
let _isOnline = navigator.onLine;
let _offlineSince = null;
const _pendingProgressSync = []; // answers that failed to sync while offline

function setNetworkStatus(online) {
    if (online === _isOnline) return;
    _isOnline = online;

    const indicator = document.getElementById('network-status-bar');
    if (indicator) {
        indicator.textContent = online ? '✓ Connection Restored' : '⚠ No Connection — Answers saving locally';
        indicator.style.background = online ? '#065f46' : '#7f1d1d';
        indicator.style.display = 'block';
        if (online) setTimeout(() => { if (indicator) indicator.style.display = 'none'; }, 3000);
    }

    if (online) {
        _offlineSince = null;
        // Flush any queued submits
        flushSubmitQueue();
        // Re-sync any answers that failed to reach the server while offline
        flushPendingProgress();
    } else {
        _offlineSince = Date.now();
    }
}

async function flushPendingProgress() {
    if (_pendingProgressSync.length === 0) return;
    const toSync = [..._pendingProgressSync];
    _pendingProgressSync.length = 0;
    for (const item of toSync) {
        try {
            await fetch(`${API_BASE}/Student/progress`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(item)
            });
        } catch(e) {
            _pendingProgressSync.push(item); // re-queue if still failing
        }
    }
}

window.addEventListener('online',  () => setNetworkStatus(true));
window.addEventListener('offline', () => setNetworkStatus(false));

// Queues failed submits and retries whenever connectivity returns.
const QUEUE_KEY = 'cbt_submit_queue';

function queueSubmit(payload) {
    let queue = [];
    try { queue = JSON.parse(localStorage.getItem(QUEUE_KEY) || '[]'); } catch(e) {}
    // Replace any existing entry for the same studentExamId
    queue = queue.filter(e => e.studentExamId !== payload.studentExamId);
    queue.push(payload);
    localStorage.setItem(QUEUE_KEY, JSON.stringify(queue));
}

function dequeueSubmit(studentExamId) {
    let queue = [];
    try { queue = JSON.parse(localStorage.getItem(QUEUE_KEY) || '[]'); } catch(e) {}
    queue = queue.filter(e => e.studentExamId !== studentExamId);
    localStorage.setItem(QUEUE_KEY, JSON.stringify(queue));
}

async function flushSubmitQueue() {
    let queue = [];
    try { queue = JSON.parse(localStorage.getItem(QUEUE_KEY) || '[]'); } catch(e) {}
    if (queue.length === 0) return;

    for (const payload of [...queue]) {
        try {
            const res = await fetch(`${API_BASE}/Student/submit`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ studentExamId: payload.studentExamId, answers: payload.answers })
            });
            if (res.ok) {
                dequeueSubmit(payload.studentExamId);
                console.log(`[Queue] Flushed queued submit for exam ${payload.studentExamId}`);
            }
        } catch(e) { /* still offline, leave in queue */ }
    }
}

// Retry queue every 15 seconds in background
setInterval(flushSubmitQueue, 15000);
// Also retry when connection comes back
window.addEventListener('online', () => setTimeout(flushSubmitQueue, 1000));

// ── Offline Score Calculator ──
// Uses the cached shuffled questions (which include correctIndex) to score locally.
function computeScoreOffline(cachedQuestions, answers) {
    const ansMap = {};
    answers.forEach(a => { ansMap[a.questionId] = a.selectedAnswer; });

    // Group by subject for JAMB scaling
    const subjectGroups = {};
    cachedQuestions.forEach(q => {
        const sub = q.subject || 'General';
        if (!subjectGroups[sub]) subjectGroups[sub] = [];
        subjectGroups[sub].push(q);
    });

    let totalCorrect = 0;
    let jambTotal = 0;
    const breakdownParts = [];

    for (const [sub, qList] of Object.entries(subjectGroups)) {
        let correct = 0;
        qList.forEach(q => {
            const selected = ansMap[q.questionId];
            if (selected === undefined) return;
            const correctOption = q.options[q.correctIndex];
            if (correctOption && selected.trim().toLowerCase() === correctOption.trim().toLowerCase()) correct++;
        });
        totalCorrect += correct;
        const pool = qList.length;
        const scaled = pool > 0 ? Math.round((correct / pool) * 100) : 0; // whole number 0-100
        jambTotal += scaled;
        breakdownParts.push(`${sub}: ${correct}/${pool} (${scaled}/100)`);
    }

    const subjectCount = Object.keys(subjectGroups).length;
    const maxScore = subjectCount * 100;
    const percentage = maxScore > 0 ? Math.round((jambTotal / maxScore) * 100) : 0;
    const jambScore = subjectCount > 0 ? Math.round(jambTotal) : null; // whole number

    // Structured per-subject array for results page score cards
    const subjectScores = Object.entries(subjectGroups).map(([sub, qList]) => {
        const pool = qList.length;
        let correct = 0;
        qList.forEach(q => {
            const selected = ansMap[q.questionId];
            if (selected === undefined) return;
            const correctOption = q.options[q.correctIndex];
            if (correctOption && selected.trim().toLowerCase() === correctOption.trim().toLowerCase()) correct++;
        });
        const scaled = pool > 0 ? Math.round((correct / pool) * 100) : 0; // whole number
        return { subject: sub, correct, pool, scaled };
    });

    return {
        score: totalCorrect,
        total: cachedQuestions.length,
        percentage,
        jambScore,
        subjectBreakdown: breakdownParts.join(' | '),
        subjectScores
    };
}

// ── Submit Exam Logic ──

function confirmSubmit() {
    if (examCompleted) return;
    // Populate confirm dialog stats
    const ans = Object.keys(responses).length;
    const tot = questions.length;
    const ca = document.getElementById('confirm-answered');
    const ct = document.getElementById('confirm-total');
    if (ca) ca.textContent = ans;
    if (ct) ct.textContent = tot;
    const modal = document.getElementById('submit-confirm-modal');
    if (modal) modal.classList.remove('hidden');
}

function closeSubmitConfirm() {
    const modal = document.getElementById('submit-confirm-modal');
    if (modal) modal.classList.add('hidden');
}

function closeModalOnOuterClick(event, modalId) {
    if (event.target.id === modalId) {
        if (modalId === 'calculator-modal') {
            toggleCalculator();
        } else if (modalId === 'submit-confirm-modal') {
            closeSubmitConfirm();
        }
    }
}

async function submitExam(manual = true) {
    if (examCompleted) return;
    examCompleted = true;

    closeSubmitConfirm();
    clearInterval(timerInterval);
    clearInterval(heartbeatInterval);

    // Collect all answers (including unanswered questions for completeness)
    const answersList = [];
    questions.forEach(q => {
        const ans = responses[q.questionId];
        if (ans !== undefined && ans !== '') {
            answersList.push({ questionId: q.questionId, selectedAnswer: ans });
        }
    });

    const seId = parseInt(studentExamId);
    if (!seId || isNaN(seId)) {
        showToast('Error', 'Missing exam session ID. Please rejoin.', 'error');
        examCompleted = false;
        return;
    }

    // Always compute score offline first
    const offlineResult = computeScoreOffline(questions, answersList);

    const examTitle = localStorage.getItem('selectedExamTitle') || 'Examination';
    const resultPayload = {
        examTitle,
        studentName: localStorage.getItem('studentName') || 'Candidate',
        studentId:   localStorage.getItem('studentId')   || '00000000',
        score:       offlineResult.score,
        total:       offlineResult.total,
        percentage:  offlineResult.percentage,
        jambScore:   offlineResult.jambScore,
        subjectBreakdown: offlineResult.subjectBreakdown,
        subjectScores: offlineResult.subjectScores || [],
        submittedAt: new Date().toLocaleString(),
        answers:  answersList,
        questions: questions
    };

    // Try submitting to server
    let serverOk = false;
    try {
        const submitRes = await fetch(`${API_BASE}/Student/submit`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ studentExamId: seId, answers: answersList })
        });

        if (submitRes.ok) {
            serverOk = true;
            const scoreResult = await submitRes.json();
            resultPayload.score      = scoreResult.score      ?? offlineResult.score;
            resultPayload.total      = scoreResult.total      ?? offlineResult.total;
            resultPayload.percentage = scoreResult.percentage ?? offlineResult.percentage;
            resultPayload.jambScore  = scoreResult.jambScore  ?? offlineResult.jambScore;
            resultPayload.subjectBreakdown = scoreResult.subjectBreakdown || offlineResult.subjectBreakdown;
        } else {
            const errText = await submitRes.text();
            console.error('[Submit] Server error:', submitRes.status, errText);
            showToast('Submit Error', `Server error ${submitRes.status}: ${errText.substring(0,120)}`, 'error');
        }
    } catch (e) {
        console.warn('[Submit] Network error, queuing for retry:', e);
    }

    if (!serverOk) {
        queueSubmit({ studentExamId: seId, answers: answersList });
        showToast('Saved Offline', 'Server unreachable. Your answers are saved and will sync automatically.', 'info');
    }

    localStorage.setItem('lastExamResult', JSON.stringify(resultPayload));

    const hasJamb = resultPayload.jambScore && resultPayload.jambScore > 0;
    const displayScore = hasJamb
        ? `${Math.round(resultPayload.jambScore)} / 400`
        : `${Math.round(resultPayload.percentage)}%`;
    document.getElementById('score-text').textContent = displayScore;
    const scoreLabelEl = document.getElementById('score-label');
    if (scoreLabelEl) scoreLabelEl.textContent = hasJamb ? 'JAMB Score' : 'Official Score';
    document.getElementById('completion-modal').classList.remove('hidden');
}

function exitExamPortal() {
    clearAllStudentData();
    window.location.href = '/results';
}

