/**
 * CBT Exam System - Student Portal Script
 * Handles login interactions and UI states
 */

function togglePasswordVisibility() {
    const passwordInput = document.getElementById('password');
    const eyeIcon = document.getElementById('eyeIcon');
    
    if (passwordInput.type === 'password') {
        passwordInput.type = 'text';
        // Eye off icon
        eyeIcon.innerHTML = `<path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/>`;
    } else {
        passwordInput.type = 'password';
        // Eye icon
        eyeIcon.innerHTML = `<path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>`;
    }
}

async function handleLogin(event) {
    event.preventDefault();
    
    const user = document.getElementById('username').value.trim();
    const pass = document.getElementById('password').value.trim();
    const btn = document.getElementById('submitBtn');
    
    if (!user || !pass) {
        alert('Please enter your credentials.');
        return;
    }

    // Update button to loading state
    const originalContent = btn.innerHTML;
    btn.disabled = true;
    btn.innerHTML = '<div class="spinner"></div><span>Signing In...</span>';

    // Simulate authentication delay for premium feel
    setTimeout(() => {
        // Redirection logic
        window.location.href = 'selection.html';
    }, 1200);
}

// Add smooth reveal animation on load
document.addEventListener('DOMContentLoaded', () => {
    console.log('CBT Portal Initialized');
});
