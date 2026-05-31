# Implementation Plan

- [ ] 1. Write bug condition exploration tests (BEFORE implementing any fix)
  - **Property 1: Bug Condition** - Duplicate Subject, Inline Styles, Student Count Zero, Heartbeat Race
  - **CRITICAL**: These tests MUST FAIL on unfixed code — failure confirms each bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: These tests encode the expected behavior — they will validate the fixes when they pass after implementation
  - **GOAL**: Surface counterexamples that demonstrate each bug exists
  - **Scoped PBT Approach**: For deterministic bugs, scope the property to the concrete failing case(s) to ensure reproducibility

  **Bug 1 — Duplicate Subject (C#/WPF):**
  - Instantiate `ExamsViewModel` with a mocked `ApiClient` that returns at least two subjects (e.g. "Mathematics", "English")
  - Add two `ExamSubjectConfigVM` rows; set `SelectedSubject = "Mathematics"` on both
  - Assert that `SaveTemplateAsync` returns an error (i.e. `StatusOk == false` and `CreateStatus` contains "duplicate")
  - Also assert that row 2's `AvailableSubjects` does NOT contain "Mathematics" after row 1 selects it
  - Run on UNFIXED code — **EXPECTED OUTCOME**: Test FAILS (save succeeds and dropdown still shows "Mathematics")
  - Document counterexample: `SubjectConfigs = [Mathematics, Mathematics]` → save proceeds without error

  **Bug 2 — Inline Styles (JavaScript/DOM):**
  - Load `exam.html` (or a minimal HTML fixture) with `renderSubjectTabs()` called with 2+ subjects
  - For each rendered button, assert `button.getAttribute('style') === null || button.getAttribute('style') === ''`
  - Run on UNFIXED code — **EXPECTED OUTCOME**: Test FAILS (buttons have non-empty `style` attributes)
  - Document counterexample: `button.style.background === '#10B981'` on the active tab

  **Bug 3a — Student Count (API):**
  - Seed an `ExamSession` with 2 `StudentExam` records in an in-memory EF Core database
  - Call `SessionsController.GetAll()` and inspect the returned `SessionDto.StudentCount`
  - Assert `studentCount == 2`
  - Run on UNFIXED code — **EXPECTED OUTCOME**: Test FAILS (`studentCount == 0`)
  - Document counterexample: session with 2 joined students returns `studentCount: 0`

  **Bug 3b — Heartbeat Race Condition (JavaScript):**
  - Simulate `startExamProcess` where the first heartbeat mock immediately returns `{ isStarted: true, decryptionKey: "key" }`
  - Spy on `clearInterval` and `window.location.href` assignment
  - Assert `clearInterval` is called with a defined (non-undefined) handle
  - Assert `window.location.href = 'exam.html'` is triggered exactly once
  - Run on UNFIXED code — **EXPECTED OUTCOME**: Test FAILS (`clearInterval(undefined)` is called; multiple redirects may fire)
  - Document counterexample: `heartbeatInterval` is `undefined` at the time `clearInterval` is called

  - Mark task complete when all four exploration tests are written, run, and failures are documented
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.6, 1.7, 1.8_

- [ ] 2. Write preservation property tests (BEFORE implementing any fix)
  - **Property 2: Preservation** - Valid Template Save, Tab Functionality, Pre-Start Polling, Non-Zero Session Count
  - **IMPORTANT**: Follow observation-first methodology — run UNFIXED code with non-buggy inputs and record actual outputs
  - **EXPECTED OUTCOME**: All preservation tests PASS on unfixed code (confirms baseline behavior to preserve)

  **Bug 1 — Valid Template Save Preserved:**
  - Observe: `SaveTemplateAsync` with `SubjectConfigs = [English, Mathematics, Physics]` (all unique) succeeds on unfixed code
  - Write property-based test: for all combinations of 1–4 unique subjects from the available list, `SaveTemplateAsync` succeeds and `StatusOk == true`
  - Also verify: selecting a subject in a row that is not used by any other row still triggers `LoadYearsAsync` and populates `AvailableYears`
  - Verify test PASSES on unfixed code

  **Bug 2 — Tab Functionality Preserved:**
  - Observe: after `renderSubjectTabs()`, the active tab has `class` containing `"btn-tab active"` and inactive tabs have `"btn-tab"` (without `"active"`)
  - Observe: clicking an inactive tab calls `switchSubject(sub)` and re-renders tabs with the new subject active
  - Write property-based test: for all subject lists of length 1–5, every rendered button has `className` containing `"btn-tab"`, exactly one button has `"active"`, and clicking any button invokes `switchSubject`
  - Verify test PASSES on unfixed code

  **Bug 3a — Non-Zero Session Count Preserved:**
  - Observe: `GetAll()` on a session with 0 `StudentExam` records returns `studentCount == 0` on unfixed code (this is correct behavior for empty sessions)
  - Write property-based test: for sessions with 0 students, `studentCount` is 0; for sessions with N > 0 students (after fix), `studentCount == N`
  - Verify the 0-student case PASSES on unfixed code

  **Bug 3b — Pre-Start Polling Preserved:**
  - Observe: when all heartbeat responses return `{ isStarted: false }`, the waiting room stays on the page, `pollingInterval` continues, and no redirect fires
  - Write property-based test: for any sequence of N heartbeat responses all with `isStarted: false`, `window.location.href` is never set to `'exam.html'` and the interval is never cleared
  - Verify test PASSES on unfixed code

  - Mark task complete when all preservation tests are written, run, and passing on unfixed code
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

- [-] 3. Fix Bug 1 — Duplicate Subject Prevention in `PageViewModels.cs`

  - [x] 3.1 Give each `ExamSubjectConfigVM` row its own filtered `AvailableSubjects` collection
    - Change `ExamSubjectConfigVM` constructor to accept `IEnumerable<string> allSubjects` and `Action<ExamSubjectConfigVM> onRefreshNeeded` instead of a shared `ObservableCollection<string>`
    - Replace the shared `AvailableSubjects` property with a new `ObservableCollection<string>` owned by the row
    - Add `RefreshAvailableSubjects(IEnumerable<string> allSubjects, string? ownSubject)` method that rebuilds the per-row list: include all subjects from `allSubjects` that are NOT already selected in sibling rows, plus the row's own current `ownSubject` (so the current selection is never removed from its own dropdown)
    - _Bug_Condition: isBugCondition_1 — row.AvailableSubjects contains a subject already selected in another row_
    - _Expected_Behavior: row.AvailableSubjects = allSubjects MINUS (selectedSubjects of other rows)_
    - _Preservation: selecting a unique subject still loads years; BatchAddSubjectsCommand guard still skips duplicates_
    - _Requirements: 2.2, 3.1, 3.2_

  - [x] 3.2 Wire up `RefreshAvailableSubjects` on selection change and row add/remove
    - In `ExamsViewModel`, change the `_onChanged` callback passed to each row to also call `RefreshAllRowSubjects()` — a new private helper that iterates `SubjectConfigs` and calls `row.RefreshAvailableSubjects(AvailableSubjects, row.SelectedSubject)` on each
    - Call `RefreshAllRowSubjects()` in `AddSubjectRow`, `RemoveSubjectRow`, and `BatchAddSubjectsCommand` after modifying `SubjectConfigs`
    - _Bug_Condition: isBugCondition_1 — dropdown shows already-selected subjects after another row's selection changes_
    - _Expected_Behavior: all rows' dropdowns update immediately when any row's selection changes_
    - _Requirements: 2.2, 3.1_

  - [x] 3.3 Add save-time duplicate check in `SaveTemplateAsync`
    - Before the existing per-row validation loop in `SaveTemplateAsync`, insert:
      ```csharp
      var duplicateSubjects = SubjectConfigs
          .GroupBy(c => c.SelectedSubject, StringComparer.OrdinalIgnoreCase)
          .Where(g => g.Count() > 1)
          .Select(g => g.Key)
          .ToList();
      if (duplicateSubjects.Count > 0)
      {
          CreateStatus = $"Duplicate subject(s): {string.Join(", ", duplicateSubjects)}. Each subject may only appear once.";
          StatusOk = false; CurrentStep = 2; return;
      }
      ```
    - _Bug_Condition: isBugCondition_1 — SubjectConfigs contains two rows with the same SelectedSubject_
    - _Expected_Behavior: save is blocked; CreateStatus contains "Duplicate subject(s)"; StatusOk == false_
    - _Preservation: valid templates with unique subjects continue to save successfully_
    - _Requirements: 2.1, 2.3, 3.3_

  - [ ] 3.4 Verify Bug 1 exploration test now passes
    - **Property 1: Expected Behavior** - Duplicate Subject Rejected
    - **IMPORTANT**: Re-run the SAME test from task 1 (Bug 1 section) — do NOT write a new test
    - The test from task 1 encodes the expected behavior: save blocked + dropdown filtered
    - **EXPECTED OUTCOME**: Test PASSES (confirms Bug 1 is fixed)
    - _Requirements: 2.1, 2.2, 2.3_

  - [ ] 3.5 Verify Bug 1 preservation tests still pass
    - **Property 2: Preservation** - Valid Template Save and Year Loading Unaffected
    - **IMPORTANT**: Re-run the SAME tests from task 2 (Bug 1 section) — do NOT write new tests
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions in template save and subject selection)

- [-] 4. Fix Bug 2 — Subject Tab Inline Styles in `app.js` and `exam.css`

  - [x] 4.1 Remove all `btn.style.*` assignments from `renderSubjectTabs()` in `app.js`
    - In `src/CbtExam.Desktop/wwwroot/js/app.js`, locate `renderSubjectTabs()`
    - Delete the entire block that sets `btn.style.padding`, `btn.style.borderRadius`, `btn.style.fontWeight`, `btn.style.cursor`, `btn.style.transition`, `btn.style.fontSize`, `btn.style.textTransform`, `btn.style.letterSpacing`, `btn.style.background`, `btn.style.color`, `btn.style.border`, and `btn.style.boxShadow`
    - Keep the existing `btn.className = \`btn btn-tab ${sub === activeSubject ? 'active' : ''}\`` line unchanged
    - Keep the `btn.onclick = () => switchSubject(sub)` assignment unchanged
    - _Bug_Condition: isBugCondition_2 — button.style.background != "" (or any other inline style property is non-empty)_
    - _Expected_Behavior: isBugCondition_2(button) == false for every rendered tab button_
    - _Preservation: btn.className still contains "btn-tab"; active tab still has "active" class; onclick still calls switchSubject_
    - _Requirements: 2.4, 3.4, 3.5_

  - [x] 4.2 Add `.btn-tab` and `.btn-tab.active` CSS rules to `exam.css`
    - In `src/CbtExam.Desktop/wwwroot/css/exam.css`, append the following rules after the existing `/* ── Navigator Buttons ── */` section:
      ```css
      /* ── Subject Tab Buttons ── */
      .btn-tab {
        padding: 10px 20px;
        border-radius: 10px;
        font-weight: 700;
        cursor: pointer;
        transition: all 0.2s ease;
        font-size: 13px;
        text-transform: uppercase;
        letter-spacing: 0.5px;
        background: rgba(255, 255, 255, 0.08);
        color: #94A3B8;
        border: 1px solid rgba(255, 255, 255, 0.1);
      }

      .btn-tab.active {
        background: #10B981;
        color: #ffffff;
        border: none;
        box-shadow: 0 4px 12px rgba(16, 185, 129, 0.25);
      }
      ```
    - _Bug_Condition: isBugCondition_2 — no CSS class rules exist to style tabs, so removing inline styles would leave tabs unstyled_
    - _Expected_Behavior: .btn-tab and .btn-tab.active rules provide all visual state previously set by inline styles_
    - _Preservation: all other exam.css rules are unaffected; .btn and .nav-btn rules remain unchanged_
    - _Requirements: 2.4, 2.5_

  - [ ] 4.3 Verify Bug 2 exploration test now passes
    - **Property 1: Expected Behavior** - No Inline Styles on Tab Buttons
    - **IMPORTANT**: Re-run the SAME test from task 1 (Bug 2 section) — do NOT write a new test
    - **EXPECTED OUTCOME**: Test PASSES (`button.getAttribute('style')` is null or empty for all tab buttons)
    - _Requirements: 2.4, 2.5_

  - [ ] 4.4 Verify Bug 2 preservation tests still pass
    - **Property 2: Preservation** - Tab Functionality and Class Assignment Unaffected
    - **IMPORTANT**: Re-run the SAME tests from task 2 (Bug 2 section) — do NOT write new tests
    - **EXPECTED OUTCOME**: Tests PASS (active class toggle and switchSubject behavior unchanged)

- [-] 5. Fix Bug 3a — Student Count Always Zero in `SessionsController.cs`

  - [x] 5.1 Add `.Include(s => s.StudentExams)` to `GetAll()` query
    - In `src/CbtExam.Api/Controllers/SessionsController.cs`, locate the `GetAll()` method
    - Change:
      ```csharp
      var sessions = await db.ExamSessions
          .Include(s => s.Exam)
          .ToListAsync();
      ```
      to:
      ```csharp
      var sessions = await db.ExamSessions
          .Include(s => s.Exam)
          .Include(s => s.StudentExams)
          .ToListAsync();
      ```
    - No other changes to `GetAll()` are needed — `s.StudentExams.Count` in the projection already reads the correct value once the collection is loaded
    - _Bug_Condition: isBugCondition_3a — session.StudentExams is empty despite students having joined_
    - _Expected_Behavior: s.StudentExams.Count == actual number of StudentExam records for the session_
    - _Preservation: all other session endpoints (GetStudents, Stop, EndAll, GetResults) are unaffected_
    - _Requirements: 2.6, 3.8, 3.9_

  - [ ] 5.2 Verify Bug 3a exploration test now passes
    - **Property 1: Expected Behavior** - Student Count Reflects Actual Joins
    - **IMPORTANT**: Re-run the SAME test from task 1 (Bug 3a section) — do NOT write a new test
    - **EXPECTED OUTCOME**: Test PASSES (session with 2 joined students returns `studentCount: 2`)
    - _Requirements: 2.6_

  - [ ] 5.3 Verify Bug 3a preservation tests still pass
    - **Property 2: Preservation** - Zero-Student Session Count Unaffected
    - **IMPORTANT**: Re-run the SAME tests from task 2 (Bug 3a section) — do NOT write new tests
    - **EXPECTED OUTCOME**: Tests PASS (sessions with 0 students still return `studentCount: 0`)

- [-] 6. Fix Bug 3b — Heartbeat Race Condition in `waiting.html`

  - [x] 6.1 Assign `heartbeatInterval` before the first `pollHeartbeatSignal` call
    - In `src/CbtExam.Desktop/wwwroot/waiting.html`, locate `startExamProcess` and find the block:
      ```javascript
      // Start Continuous LAN Heartbeat every 3 seconds
      pollHeartbeatSignal(studentExamId, activeDeviceId, activeDeviceName);
      heartbeatInterval = setInterval(() => {
          pollHeartbeatSignal(studentExamId, activeDeviceId, activeDeviceName);
      }, 3000);
      ```
    - Swap the order so `setInterval` is assigned first:
      ```javascript
      // Assign interval FIRST so clearInterval always has a valid handle
      heartbeatInterval = setInterval(() => {
          pollHeartbeatSignal(studentExamId, activeDeviceId, activeDeviceName);
      }, 3000);
      // Then fire the first poll immediately
      pollHeartbeatSignal(studentExamId, activeDeviceId, activeDeviceName);
      ```
    - _Bug_Condition: isBugCondition_3b — heartbeatInterval IS undefined at the time clearInterval() is called inside pollHeartbeatSignal_
    - _Expected_Behavior: heartbeatInterval is always a valid handle when clearInterval is called; interval is cancelled exactly once; exactly one redirect fires_
    - _Preservation: polling continues at 3-second intervals when isStarted is false; single redirect fires when isStarted becomes true_
    - _Requirements: 2.7, 2.8, 3.6, 3.7_

  - [x] 6.2 Add early-exit in `checkSession` when `isStarted` is already `true`
    - In `src/CbtExam.Desktop/wwwroot/waiting.html`, locate the `checkSession` function
    - After the existing `if (downloading) return;` guard, add a check for the already-started case:
      ```javascript
      const checkSession = async () => {
          if (downloading) return;
          try {
              const response = await fetch(`${API_BASE}/Student/active-session/${selectedExamId}`);
              if (!response.ok) return;

              const data = await response.json();
              if (data.found && data.isActive) {
                  clearInterval(pollingInterval);
                  currentSession = {
                      id: data.id,
                      examId: data.examId,
                      examTitle: data.examTitle,
                      sessionCode: data.sessionCode,
                      isActive: data.isActive,
                      isStarted: data.isStarted
                  };
                  startExamProcess(currentSession);
              }
          } catch (err) {
              // Silent — network may not be ready yet
          }
      };
      ```
    - Inside `startExamProcess`, after the questions are downloaded and cached, check `if (session.isStarted)` before starting the heartbeat loop. If already started, skip the heartbeat loop and call `triggerCountdown()` directly (after decrypting with a key obtained from a dedicated endpoint or by proceeding to the exam page directly)
    - _Bug_Condition: isBugCondition_3b — student arrives after exam already started; first heartbeat returns isStarted: true before heartbeatInterval is assigned_
    - _Expected_Behavior: if session.isStarted is true at join time, skip heartbeat loop and proceed directly to countdown_
    - _Preservation: students who arrive before exam starts continue to poll normally; pre-start flow is unchanged_
    - _Requirements: 2.7, 3.6_

  - [ ] 6.3 Verify Bug 3b exploration test now passes
    - **Property 1: Expected Behavior** - Heartbeat Interval Always Cleared Before Redirect
    - **IMPORTANT**: Re-run the SAME test from task 1 (Bug 3b section) — do NOT write a new test
    - **EXPECTED OUTCOME**: Test PASSES (`clearInterval` called with a defined handle; exactly one redirect fires)
    - _Requirements: 2.7, 2.8_

  - [ ] 6.4 Verify Bug 3b preservation tests still pass
    - **Property 2: Preservation** - Pre-Start Polling Behavior Unaffected
    - **IMPORTANT**: Re-run the SAME tests from task 2 (Bug 3b section) — do NOT write new tests
    - **EXPECTED OUTCOME**: Tests PASS (no redirect fires when all heartbeats return `isStarted: false`)

- [ ] 7. Checkpoint — Ensure all tests pass
  - Re-run the full test suite (all exploration tests from task 1 and all preservation tests from task 2)
  - Confirm all four Bug Condition exploration tests now PASS (bugs are fixed)
  - Confirm all four Preservation property tests still PASS (no regressions)
  - Manually verify in the running application:
    - Template builder: add two rows, attempt to select the same subject in both — second row's dropdown should not show the already-selected subject; attempting to save should show a duplicate error
    - Exam page: inspect subject tab buttons in DevTools — no inline `style` attribute should be present; tabs should be styled by CSS classes only
    - Sessions dashboard: start a session, have a student join, check the session card — student count should show 1 (not 0)
    - Waiting room: start the exam before a student arrives, then open the waiting room — student should be redirected to `exam.html` exactly once after the countdown
  - Ensure all tests pass; ask the user if questions arise.
