// ── State ──────────────────────────────────────────────────────────────────
const state = {
  studentExamId: null,
  sessionId: null,
  examId: null,
  durationMinutes: 0,
  questions: [],       // ShuffledQuestionDto[]
  answers: {},         // { questionId: selectedAnswer }
  currentIndex: 0,
  timerInterval: null,
  secondsLeft: 0,
  submitted: false
};

// ── Helpers ────────────────────────────────────────────────────────────────
const $ = id => document.getElementById(id);
const showPage = id => {
  document.querySelectorAll('.page').forEach(p => p.classList.remove('active'));
  $(id).classList.add('active');
};

// Pre-fill session code from URL ?code=XXXXXX
window.addEventListener('DOMContentLoaded', () => {
  const code = new URLSearchParams(location.search).get('code');
  if (code) $('inp-code').value = code.toUpperCase();
});

// ── Anti-cheat: Tab Switch Detection ──────────────────────────────────────
document.addEventListener('visibilitychange', () => {
  if (document.hidden && state.studentExamId && !state.submitted) {
    fetch('/api/student/tabswitch', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ studentExamId: state.studentExamId })
    });
    showWarning('⚠ Tab switching detected and logged!');
  }
});

// ── Anti-cheat: Fullscreen ─────────────────────────────────────────────────
function requestFullscreen() {
  const el = document.documentElement;
  if (el.requestFullscreen) el.requestFullscreen();
  else if (el.webkitRequestFullscreen) el.webkitRequestFullscreen();
}

document.addEventListener('fullscreenchange', () => {
  if (!document.fullscreenElement && state.studentExamId && !state.submitted) {
    showWarning('⚠ Please stay in fullscreen mode!');
    setTimeout(requestFullscreen, 1500);
  }
});

// ── Anti-cheat: Prevent right-click & keyboard shortcuts ──────────────────
document.addEventListener('contextmenu', e => { if (state.studentExamId) e.preventDefault(); });
document.addEventListener('keydown', e => {
  if (!state.studentExamId) return;
  if ((e.ctrlKey || e.metaKey) && ['c','v','a','p','s','u'].includes(e.key.toLowerCase())) e.preventDefault();
  if (e.key === 'F12' || e.key === 'F5') e.preventDefault();
});

// ── Prevent accidental page refresh ───────────────────────────────────────
window.addEventListener('beforeunload', e => {
  if (state.studentExamId && !state.submitted) {
    e.preventDefault();
    e.returnValue = '';
  }
});

// ── Join Exam ──────────────────────────────────────────────────────────────
async function joinExam() {
  const code = $('inp-code').value.trim().toUpperCase();
  const name = $('inp-name').value.trim();
  const id   = $('inp-id').value.trim();
  const errEl = $('join-error');

  if (!code || !name || !id) { showError(errEl, 'All fields are required.'); return; }

  $('btn-join').disabled = true;
  $('btn-join').textContent = 'Joining...';

  try {
    const res = await fetch('/api/student/join', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sessionCode: code, fullName: name, studentId: id })
    });

    if (!res.ok) {
      const msg = await res.text();
      showError(errEl, msg || 'Failed to join. Check session code.');
      return;
    }

    const data = await res.json();
    state.studentExamId = data.studentExamId;
    state.sessionId = data.sessionId;
    state.examId = data.examId;
    state.durationMinutes = data.durationMinutes;

    $('exam-title').textContent = data.examTitle;
    errEl.classList.add('hidden');

    await loadQuestions();
    requestFullscreen();
    showPage('page-exam');
    startTimer(state.durationMinutes * 60);

  } catch (err) {
    showError(errEl, 'Connection error. Is the server running?');
  } finally {
    $('btn-join').disabled = false;
    $('btn-join').textContent = 'Join Exam →';
  }
}

// ── Load Questions ─────────────────────────────────────────────────────────
async function loadQuestions() {
  const res = await fetch(`/api/student/${state.studentExamId}/questions`);
  state.questions = await res.json();
  state.currentIndex = 0;
  renderNavigator();
  renderQuestion();
}

// ── Render Question Navigator ──────────────────────────────────────────────
function renderNavigator() {
  const nav = $('question-nav');
  nav.innerHTML = '<h4>Questions</h4><div class="nav-grid" id="nav-grid"></div>';
  const grid = $('nav-grid');
  state.questions.forEach((q, i) => {
    const btn = document.createElement('button');
    btn.className = 'nav-btn';
    btn.textContent = i + 1;
    btn.onclick = () => goToQuestion(i);
    btn.id = `nav-${i}`;
    grid.appendChild(btn);
  });
  updateNavigator();
}

function updateNavigator() {
  state.questions.forEach((q, i) => {
    const btn = $(`nav-${i}`);
    if (!btn) return;
    btn.className = 'nav-btn';
    if (state.answers[q.questionId] !== undefined) btn.classList.add('answered');
    if (i === state.currentIndex) btn.classList.add('current');
  });
  const answered = Object.keys(state.answers).length;
  $('progress-text').textContent = `${answered} / ${state.questions.length}`;
}

// ── Render Current Question ────────────────────────────────────────────────
function renderQuestion() {
  const q = state.questions[state.currentIndex];
  if (!q) return;

  const letters = ['A', 'B', 'C', 'D', 'E', 'F'];
  const selected = state.answers[q.questionId];

  $('question-container').innerHTML = `
    <div class="question-card">
      <div class="question-number">Question ${state.currentIndex + 1} of ${state.questions.length}</div>
      <div class="question-text">${escapeHtml(q.text)}</div>
      <div class="options-list">
        ${q.options.map((opt, i) => `
          <div class="option-item ${selected === opt ? 'selected' : ''}" onclick="selectAnswer('${escapeAttr(opt)}')">
            <div class="option-letter">${letters[i] || i + 1}</div>
            <div class="option-text">${escapeHtml(opt)}</div>
          </div>
        `).join('')}
      </div>
    </div>`;

  $('btn-prev').disabled = state.currentIndex === 0;
  $('btn-next').disabled = state.currentIndex === state.questions.length - 1;
  updateNavigator();
}

// ── Answer Selection ───────────────────────────────────────────────────────
function selectAnswer(optionText) {
  const q = state.questions[state.currentIndex];
  state.answers[q.questionId] = optionText;
  renderQuestion();
}

// ── Navigation ─────────────────────────────────────────────────────────────
function goToQuestion(i) { state.currentIndex = i; renderQuestion(); }
function prevQuestion() { if (state.currentIndex > 0) { state.currentIndex--; renderQuestion(); } }
function nextQuestion() { if (state.currentIndex < state.questions.length - 1) { state.currentIndex++; renderQuestion(); } }

// ── Timer ──────────────────────────────────────────────────────────────────
function startTimer(seconds) {
  state.secondsLeft = seconds;
  updateTimerDisplay();
  state.timerInterval = setInterval(() => {
    state.secondsLeft--;
    updateTimerDisplay();
    if (state.secondsLeft <= 300) $('timer').parentElement.classList.add('warning');
    if (state.secondsLeft <= 0) { clearInterval(state.timerInterval); submitExam(); }
  }, 1000);
}

function updateTimerDisplay() {
  const m = Math.floor(state.secondsLeft / 60).toString().padStart(2, '0');
  const s = (state.secondsLeft % 60).toString().padStart(2, '0');
  $('timer').textContent = `${m}:${s}`;
}

// ── Submit ─────────────────────────────────────────────────────────────────
function confirmSubmit() {
  const unanswered = state.questions.length - Object.keys(state.answers).length;
  $('modal-msg').textContent = unanswered > 0
    ? `You have ${unanswered} unanswered question(s). Submit anyway?`
    : 'Are you sure you want to submit? You cannot change answers after submission.';
  $('modal-overlay').classList.remove('hidden');
}

function closeModal() { $('modal-overlay').classList.add('hidden'); }

async function submitExam() {
  closeModal();
  if (state.submitted) return;
  state.submitted = true;
  clearInterval(state.timerInterval);

  const answers = Object.entries(state.answers).map(([qId, ans]) => ({
    questionId: parseInt(qId),
    selectedAnswer: ans
  }));

  try {
    const res = await fetch('/api/student/submit', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ studentExamId: state.studentExamId, answers })
    });
    const result = await res.json();
    showResult(result);
  } catch {
    showResult({ score: 0, total: state.questions.length, percentage: 0 });
  }
}

function showResult(result) {
  if (document.fullscreenElement) document.exitFullscreen?.();
  $('result-score').textContent = result.score;
  $('result-total').textContent = result.total;
  $('result-pct').textContent = `${result.percentage}%`;
  const pct = result.percentage;
  $('result-icon').textContent = pct >= 70 ? '🎉' : pct >= 50 ? '👍' : '📚';
  $('result-msg').textContent = pct >= 70 ? 'Excellent work!' : pct >= 50 ? 'Good effort!' : 'Keep studying!';
  showPage('page-result');
}

// ── Utilities ──────────────────────────────────────────────────────────────
function showError(el, msg) { el.textContent = msg; el.classList.remove('hidden'); }

let warningTimeout;
function showWarning(msg) {
  clearTimeout(warningTimeout);
  let w = $('warning-toast');
  if (!w) {
    w = document.createElement('div');
    w.id = 'warning-toast';
    w.style.cssText = 'position:fixed;top:70px;left:50%;transform:translateX(-50%);background:#EF4444;color:white;padding:10px 20px;border-radius:8px;font-weight:600;z-index:9999;font-size:14px;';
    document.body.appendChild(w);
  }
  w.textContent = msg;
  w.style.display = 'block';
  warningTimeout = setTimeout(() => { w.style.display = 'none'; }, 3000);
}

function escapeHtml(str) {
  return String(str).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}
function escapeAttr(str) {
  return String(str).replace(/'/g, '&#39;').replace(/"/g, '&quot;');
}
