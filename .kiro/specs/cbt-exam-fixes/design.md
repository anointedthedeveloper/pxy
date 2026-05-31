# CBT Exam Fixes — Bugfix Design

## Overview

This document covers the design for three targeted bug fixes in the WPF + ASP.NET Core CBT exam system:

1. **Duplicate Subject Prevention** — `ExamsViewModel.SubjectConfigs` allows the same subject to be selected in multiple `ExamSubjectConfigVM` rows. The fix adds dynamic per-row filtering of `AvailableSubjects` and a save-time duplicate check in `SaveTemplateAsync`.

2. **Subject Tab Inline Styles** — `renderSubjectTabs()` in `app.js` sets inline `btn.style.*` properties that override CSS class rules. The fix removes all inline styles and delegates all visual state to `.btn-tab` and `.btn-tab.active` CSS classes in `exam.css`.

3. **Waiting Room Race Condition + Student Count Always Zero** — Two sub-bugs: `SessionsController.GetAll()` omits `.Include(s => s.StudentExams)` so `s.StudentExams.Count` is always 0; and in `waiting.html`, if `isStarted` is already `true` on the very first heartbeat response, `heartbeatInterval` may not yet be assigned when `clearInterval` is called, leaving the interval running and potentially triggering multiple redirects.

Each fix is minimal and surgical — no surrounding code is restructured.

---

## Glossary

- **Bug_Condition (C)**: The specific input or state that triggers the defective behavior.
- **Property (P)**: The correct behavior that must hold for all inputs satisfying C.
- **Preservation**: Existing correct behaviors that must remain unchanged after the fix.
- **ExamsViewModel**: The WPF ViewModel in `PageViewModels.cs` that manages the exam template builder UI, including the `SubjectConfigs` collection of `ExamSubjectConfigVM` rows.
- **ExamSubjectConfigVM**: A per-row ViewModel representing one subject configuration in the template builder. Holds `AvailableSubjects` (the ComboBox source) and `SelectedSubject`.
- **AvailableSubjects (global)**: The `ObservableCollection<string>` on `ExamsViewModel` populated from the question bank — the master list of all subjects.
- **AvailableSubjects (per-row)**: The `ObservableCollection<string>` on each `ExamSubjectConfigVM` — currently a direct reference to the global list, which is the root cause of Bug 1.
- **renderSubjectTabs()**: The JavaScript function in `app.js` that creates subject tab buttons on the exam page. Currently sets inline `style.*` properties, which is the root cause of Bug 2.
- **heartbeatInterval**: The `setInterval` handle in `waiting.html` used to poll the heartbeat endpoint. The race condition occurs because the first async heartbeat call can resolve before the `setInterval` assignment completes.
- **isStarted**: A boolean flag returned by the heartbeat endpoint indicating the coordinator has begun the exam session.
- **starting**: A local boolean guard in `waiting.html` that prevents duplicate redirect logic from running.
- **SessionsController.GetAll()**: The API endpoint that returns all exam sessions. Missing `.Include(s => s.StudentExams)` causes lazy-load failure and always returns `StudentCount = 0`.

---

## Bug Details

### Bug 1 — Duplicate Subject Prevention in ASAM Template Builder

#### Bug Condition

The bug manifests when a user selects a subject in a row's ComboBox that is already selected in another row. Because every `ExamSubjectConfigVM` row receives a direct reference to the same `ObservableCollection<string> AvailableSubjects` on `ExamsViewModel`, all rows always show the full subject list with no filtering. There is also no duplicate check in `SaveTemplateAsync` before persisting.

**Formal Specification:**
```
FUNCTION isBugCondition_1(subjectConfigs)
  INPUT: subjectConfigs — list of ExamSubjectConfigVM rows
  OUTPUT: boolean

  selectedSubjects := [row.SelectedSubject FOR row IN subjectConfigs
                       WHERE NOT IsNullOrWhiteSpace(row.SelectedSubject)]
  RETURN selectedSubjects.Count != selectedSubjects.Distinct().Count()
         OR (ANY row IN subjectConfigs WHERE
               row.AvailableSubjects CONTAINS ANY subject
               THAT IS ALREADY SelectedSubject IN another row)
END FUNCTION
```

#### Examples

- User adds two rows and selects "Mathematics" in both → system accepts both selections; template saves with duplicate subject.
- User opens the ComboBox in row 2 after row 1 already has "English" selected → "English" still appears in row 2's dropdown.
- User clicks "Create Template" with rows [English, English, Mathematics] → save proceeds without error.
- User adds a single row and selects "Physics" (no other row uses it) → no bug; selection is accepted normally.

---

### Bug 2 — Subject Tab Inline Styles on Exam Page

#### Bug Condition

The bug manifests whenever `renderSubjectTabs()` is called (on exam page load and on every subject switch). The function sets `btn.style.padding`, `btn.style.borderRadius`, `btn.style.fontWeight`, `btn.style.background`, `btn.style.color`, `btn.style.border`, and `btn.style.boxShadow` directly on each button element. Inline styles have the highest CSS specificity and override any class-based rules in `exam.css`.

**Formal Specification:**
```
FUNCTION isBugCondition_2(button)
  INPUT: button — a DOM button element created by renderSubjectTabs()
  OUTPUT: boolean

  RETURN button.style.background != ""
      OR button.style.color != ""
      OR button.style.border != ""
      OR button.style.padding != ""
      OR button.style.borderRadius != ""
      OR button.style.fontWeight != ""
      OR button.style.boxShadow != ""
END FUNCTION
```

#### Examples

- Exam page loads with 3 subjects → each tab button has `style="background: #10B981; color: #ffffff; ..."` overriding `.btn-tab.active` CSS.
- User switches subject → `renderSubjectTabs()` re-runs, re-applying inline styles to all buttons.
- A CSS theme update changes `.btn-tab` colors → the change has no visible effect because inline styles win.
- A button has only `class="btn btn-tab active"` and no inline styles → CSS rules apply correctly (desired state).

---

### Bug 3 — Waiting Room Race Condition + Student Count Always Zero

#### Bug Condition (3a) — Student Count Always Zero

`SessionsController.GetAll()` queries `db.ExamSessions.Include(s => s.Exam)` but does NOT include `.Include(s => s.StudentExams)`. EF Core does not lazy-load navigation properties by default, so `s.StudentExams` is an empty collection and `s.StudentExams.Count` always returns 0.

**Formal Specification:**
```
FUNCTION isBugCondition_3a(session)
  INPUT: session — an ExamSession entity returned by GetAll()
  OUTPUT: boolean

  RETURN session.StudentExams IS NULL
      OR (session.StudentExams.Count == 0
          AND actual students have joined session)
END FUNCTION
```

#### Bug Condition (3b) — Heartbeat Race Condition

The race condition manifests when `isStarted` is already `true` on the very first heartbeat response. In `startExamProcess`, `pollHeartbeatSignal` is called once immediately (synchronously scheduled), and then `heartbeatInterval = setInterval(...)` is assigned. If the first `pollHeartbeatSignal` call resolves before the `setInterval` line executes (possible because `await fetch(...)` is async), `clearInterval(heartbeatInterval)` inside `pollHeartbeatSignal` is called with `undefined`. The interval is then assigned after the fact and never cleared, causing repeated calls to `pollHeartbeatSignal` and potentially multiple redirects.

**Formal Specification:**
```
FUNCTION isBugCondition_3b(session, firstHeartbeatResponse)
  INPUT: session — the session object from active-session endpoint
         firstHeartbeatResponse — the response from the first heartbeat call
  OUTPUT: boolean

  RETURN firstHeartbeatResponse.isStarted == true
         AND firstHeartbeatResponse.decryptionKey != ""
         AND heartbeatInterval IS undefined AT TIME OF clearInterval() call
END FUNCTION
```

#### Examples

- Admin starts the exam before any student arrives → student opens waiting room → first heartbeat immediately returns `isStarted: true` → `clearInterval(undefined)` is called → interval is assigned after → multiple redirects fire.
- Admin starts the exam while a student is mid-join → same race window exists between `pollHeartbeatSignal()` call and `heartbeatInterval = setInterval(...)` assignment.
- Student arrives before exam starts → heartbeat returns `isStarted: false` multiple times → no race condition; `heartbeatInterval` is assigned before any `true` response arrives.
- Session has 5 students joined → admin views session card → student count shows 0 instead of 5.

---

## Expected Behavior

### Preservation Requirements

**Bug 1 — Unchanged Behaviors:**
- Selecting a subject in a row that is not used by any other row must continue to work and load available years for that subject.
- The `BatchAddSubjectsCommand` guard that skips already-present subjects must continue to function.
- Saving a valid template with no duplicate subjects and all required fields must continue to succeed.
- Removing a subject row must continue to work and the removed subject must become available again in other rows' dropdowns.

**Bug 2 — Unchanged Behaviors:**
- The active subject tab must continue to appear visually distinct from inactive tabs.
- Clicking a tab must continue to switch the question view to that subject.
- The `btn btn-tab` class assignment and the `active` class toggle must remain in place.
- All other exam page functionality (navigation, answer selection, timer, submission) must be unaffected.

**Bug 3 — Unchanged Behaviors:**
- Students who arrive before the exam starts must continue to poll, join, download encrypted questions, and wait for the heartbeat signal.
- When `isStarted` is `false`, the waiting room must continue to remain on the page and continue polling.
- The 5-second countdown and redirect to `exam.html` must continue to work correctly.
- `SessionsController.GetStudents()`, `Stop()`, `EndAll()`, and all other session endpoints must be unaffected.
- The admin session audit history must continue to display all past sessions correctly.

**Scope:**
All inputs that do NOT satisfy the respective bug conditions above must be completely unaffected by these fixes.

---

## Hypothesized Root Cause

### Bug 1 — Duplicate Subject Prevention

1. **Shared Collection Reference**: `ExamSubjectConfigVM` is constructed with `AvailableSubjects` passed directly from `ExamsViewModel`. All rows share the same `ObservableCollection<string>` reference, so filtering one row's list would affect all rows. The fix requires each row to maintain its own filtered view.

2. **No Save-Time Validation**: `SaveTemplateAsync` validates pool sizes, year selections, and question counts, but has no `GroupBy` check for duplicate `SelectedSubject` values across rows.

3. **No Selection-Time Guard**: The `SelectedSubject` setter in `ExamSubjectConfigVM` calls `LoadYearsAsync()` but does not check whether the chosen subject is already in use by a sibling row.

### Bug 2 — Subject Tab Inline Styles

1. **Direct Style Assignment**: The original developer applied inline styles for visual customization instead of defining CSS classes. The comment `// Custom styling for elegant glassmorphic/flat modern tab buttons` confirms this was intentional but architecturally incorrect — inline styles always win over class-based rules regardless of specificity.

2. **Missing CSS Class Definitions**: `exam.css` defines `.btn` and `.nav-btn` but has no `.btn-tab` or `.btn-tab.active` rules. The fix must add these rules to `exam.css` and remove the inline assignments from `app.js`.

### Bug 3a — Student Count Always Zero

1. **Missing EF Core Include**: `GetAll()` uses `.Include(s => s.Exam)` but omits `.Include(s => s.StudentExams)`. Without eager loading, `s.StudentExams` is an uninitialized navigation property (empty collection in EF Core 8 with no lazy loading configured), so `.Count` always returns 0.

### Bug 3b — Heartbeat Race Condition

1. **Async/Sync Ordering**: `pollHeartbeatSignal(...)` is called synchronously (no `await`), but it is an `async` function. The microtask queue can resolve the first `fetch` before the JavaScript engine reaches the `heartbeatInterval = setInterval(...)` line on the next synchronous tick, particularly when the server responds very quickly (LAN environment).

2. **`starting` Flag Insufficient**: The `starting` flag prevents re-entry into the redirect logic on subsequent heartbeat calls, but it does not prevent the interval from being left running after `clearInterval(undefined)` is called.

---

## Correctness Properties

Property 1: Bug Condition — Duplicate Subject Rejected at Selection and Save Time

_For any_ `ExamsViewModel` state where `isBugCondition_1(subjectConfigs)` holds (a subject is selected in more than one row, or a row's dropdown shows an already-selected subject), the fixed system SHALL prevent the duplicate selection from being committed — either by filtering the subject out of the row's dropdown before selection, or by displaying an error and blocking the save operation.

**Validates: Requirements 2.1, 2.2, 2.3**

Property 2: Preservation — Non-Duplicate Subject Selection Unaffected

_For any_ `ExamsViewModel` state where `isBugCondition_1` does NOT hold (all selected subjects are unique), the fixed system SHALL produce exactly the same behavior as the original system: subject selection succeeds, years load, and saving proceeds without additional errors.

**Validates: Requirements 3.1, 3.2, 3.3**

Property 3: Bug Condition — No Inline Styles on Subject Tab Buttons

_For any_ call to `renderSubjectTabs()`, the fixed function SHALL produce button elements where `isBugCondition_2(button)` is false for every button — i.e., no inline `style` properties are set, and all visual state is expressed exclusively through CSS class names.

**Validates: Requirements 2.4, 2.5**

Property 4: Preservation — Subject Tab Functionality Unaffected

_For any_ subject switch or page load, the fixed `renderSubjectTabs()` SHALL continue to assign `class="btn btn-tab active"` to the active tab and `class="btn btn-tab"` to inactive tabs, and clicking any tab SHALL continue to invoke `switchSubject(sub)`.

**Validates: Requirements 3.4, 3.5**

Property 5: Bug Condition — Student Count Reflects Actual Joins

_For any_ `ExamSession` that has one or more `StudentExam` records, the fixed `SessionsController.GetAll()` SHALL return a `SessionDto` where `StudentCount` equals the actual number of joined students (i.e., `s.StudentExams.Count > 0` when students have joined).

**Validates: Requirements 2.6**

Property 6: Bug Condition — Heartbeat Interval Always Cleared Before Redirect

_For any_ execution of `startExamProcess` where the first or any subsequent heartbeat response has `isStarted: true` and a valid `decryptionKey`, the fixed waiting room code SHALL ensure `heartbeatInterval` is assigned before `clearInterval` is called, so the interval is always cancelled and exactly one redirect to `exam.html` occurs.

**Validates: Requirements 2.7, 2.8**

Property 7: Preservation — Pre-Start Waiting Room Behavior Unaffected

_For any_ execution of `startExamProcess` where all heartbeat responses have `isStarted: false`, the fixed waiting room code SHALL continue to poll at the same interval, remain on the waiting room page, and not trigger any redirect.

**Validates: Requirements 3.6, 3.7**

---

## Fix Implementation

### Bug 1 — Duplicate Subject Prevention

**File**: `src/CbtExam.Desktop/ViewModels/PageViewModels.cs`

**Changes Required:**

1. **Give each row its own filtered `AvailableSubjects`**: Change `ExamSubjectConfigVM` to hold its own `ObservableCollection<string>` instead of a shared reference. Add a `RefreshAvailableSubjects(IEnumerable<string> allSubjects, string? ownSubject)` method that rebuilds the per-row list from the global list, excluding subjects already selected in other rows (except the row's own current selection).

2. **Wire up refresh on selection change**: In `ExamsViewModel`, after any row's `SelectedSubject` changes (via the `_onChanged` callback), call `RefreshAvailableSubjects` on all rows, passing the global `AvailableSubjects` and each row's own current selection.

3. **Wire up refresh on row add/remove**: Call the same refresh logic in `AddSubjectRow`, `RemoveSubjectRow`, and `BatchAddSubjectsCommand` after modifying `SubjectConfigs`.

4. **Add save-time duplicate check in `SaveTemplateAsync`**: Before the existing per-row validation loop, add:
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

**File**: `src/CbtExam.Desktop/Views/ExamsView.xaml`

5. **Update ComboBox binding**: The `ComboBox` in the `ExamSubjectConfigVM` `DataTemplate` already binds to `AvailableSubjects` on the row VM — no XAML change needed once the per-row collection is in place.

---

### Bug 2 — Subject Tab Inline Styles

**File**: `src/CbtExam.Desktop/wwwroot/js/app.js`

**Function**: `renderSubjectTabs()`

**Specific Changes:**

1. **Remove all `btn.style.*` assignments**: Delete the entire block that sets `btn.style.padding`, `btn.style.borderRadius`, `btn.style.fontWeight`, `btn.style.cursor`, `btn.style.transition`, `btn.style.fontSize`, `btn.style.textTransform`, `btn.style.letterSpacing`, `btn.style.background`, `btn.style.color`, `btn.style.border`, and `btn.style.boxShadow`.

2. **Rely solely on CSS classes**: The existing `btn.className = \`btn btn-tab ${sub === activeSubject ? 'active' : ''}\`` line already sets the correct classes. No JavaScript change beyond removing the inline style block is needed.

**File**: `src/CbtExam.Desktop/wwwroot/css/exam.css`

3. **Add `.btn-tab` and `.btn-tab.active` rules** that replicate the visual intent of the removed inline styles:
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

---

### Bug 3a — Student Count Always Zero

**File**: `src/CbtExam.Api/Controllers/SessionsController.cs`

**Function**: `GetAll()`

**Specific Change:**

1. **Add `.Include(s => s.StudentExams)`** to the EF Core query:
   ```csharp
   var sessions = await db.ExamSessions
       .Include(s => s.Exam)
       .Include(s => s.StudentExams)   // ← add this line
       .ToListAsync();
   ```

---

### Bug 3b — Heartbeat Race Condition

**File**: `src/CbtExam.Desktop/wwwroot/waiting.html`

**Specific Changes:**

1. **Assign `heartbeatInterval` before the first call**: Move the `setInterval` assignment to before the initial `pollHeartbeatSignal` call, so the handle is always defined when `clearInterval` is invoked:
   ```javascript
   // Assign interval FIRST so clearInterval always has a valid handle
   heartbeatInterval = setInterval(() => {
       pollHeartbeatSignal(studentExamId, activeDeviceId, activeDeviceName);
   }, 3000);
   // Then fire the first poll immediately
   pollHeartbeatSignal(studentExamId, activeDeviceId, activeDeviceName);
   ```

2. **Early-exit if `isStarted` is already true at session detection time**: In `checkSession`, after `currentSession` is populated, check `data.isStarted`. If it is already `true`, skip the heartbeat polling loop entirely and proceed directly to join + decrypt + countdown. This eliminates the race window for students who arrive after the exam has already started.

---

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate each bug on unfixed code, then verify the fix works correctly and preserves existing behavior.

---

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate each bug BEFORE implementing the fix. Confirm or refute the root cause analysis.

#### Bug 1 — Duplicate Subject

**Test Plan**: In the WPF UI, add two subject rows and select the same subject in both. Attempt to save. Observe that no error is shown and the template is saved with duplicate subjects. Also observe that the second row's ComboBox shows all subjects including the one already selected in row 1.

**Test Cases**:
1. **Duplicate Selection Accepted**: Add two rows, select "Mathematics" in both → save succeeds (will fail on unfixed code — expected to block).
2. **Dropdown Shows Used Subject**: Add row 1 with "English", open row 2 dropdown → "English" appears in the list (will fail on unfixed code — expected to be absent).
3. **Three-Row Duplicate**: Add three rows, select "Physics" in rows 1 and 3 → save succeeds (will fail on unfixed code).
4. **Single Row No Duplicate**: Add one row with "Chemistry" → save proceeds normally (should pass on both fixed and unfixed code).

**Expected Counterexamples**:
- Template saves with `[English, English, Mathematics]` subject configuration.
- All rows show identical dropdown contents regardless of other rows' selections.

#### Bug 2 — Inline Styles

**Test Plan**: Load the exam page with multiple subjects. Inspect the DOM of subject tab buttons. Observe that `button.style.background` and other inline style properties are non-empty strings.

**Test Cases**:
1. **Active Tab Has Inline Style**: Load exam page → inspect active tab → `btn.style.background === '#10B981'` (will fail on unfixed code — expected to be empty).
2. **Inactive Tab Has Inline Style**: Inspect inactive tab → `btn.style.background === 'rgba(255, 255, 255, 0.08)'` (will fail on unfixed code).
3. **CSS Override Blocked**: Add a rule `.btn-tab { background: red !important }` → tabs still show green (demonstrates inline style wins).
4. **Switch Subject Re-applies Inline Styles**: Click a different subject tab → inline styles are re-applied on every render.

**Expected Counterexamples**:
- `button.getAttribute('style')` returns a non-empty string for every tab button.

#### Bug 3a — Student Count

**Test Plan**: Start a session, have a student join, then call `GET /api/Sessions` and inspect the `studentCount` field in the response.

**Test Cases**:
1. **Count After Join**: One student joins session → `GET /api/Sessions` returns `studentCount: 0` (will fail on unfixed code — expected to return 1).
2. **Count After Multiple Joins**: Three students join → `studentCount: 0` (will fail on unfixed code — expected to return 3).

**Expected Counterexamples**:
- `sessionDto.studentCount` is always 0 regardless of how many students have joined.

#### Bug 3b — Race Condition

**Test Plan**: Simulate the race condition by starting the exam before any student arrives, then opening the waiting room. Observe whether multiple redirects fire or whether `clearInterval` is called with `undefined`.

**Test Cases**:
1. **Already-Started Session**: Admin starts exam → student opens waiting room → first heartbeat returns `isStarted: true` → observe `console.log` or breakpoint showing `heartbeatInterval` is `undefined` at `clearInterval` call time.
2. **Multiple Redirect Attempts**: Same scenario → observe that `window.location.href = 'exam.html'` is called more than once (multiple countdown overlays appear).
3. **Normal Flow Unaffected**: Student arrives before exam starts → heartbeat returns `isStarted: false` → no race condition; single redirect fires when exam starts.

**Expected Counterexamples**:
- `clearInterval(undefined)` is called (no-op), interval continues running, multiple redirects fire.

---

### Fix Checking

**Goal**: Verify that for all inputs where each bug condition holds, the fixed code produces the expected behavior.

**Pseudocode (Bug 1):**
```
FOR ALL subjectConfigs WHERE isBugCondition_1(subjectConfigs) DO
  result := attemptSave(subjectConfigs)
  ASSERT result.isError == true
  ASSERT result.errorMessage CONTAINS "duplicate"
  
  FOR EACH row IN subjectConfigs DO
    ASSERT row.AvailableSubjects NOT CONTAINS
      (SelectedSubject OF any OTHER row)
  END FOR
END FOR
```

**Pseudocode (Bug 2):**
```
FOR ALL calls TO renderSubjectTabs() DO
  FOR EACH button IN renderedButtons DO
    ASSERT isBugCondition_2(button) == false
    ASSERT button.className CONTAINS "btn-tab"
    IF button IS activeTab THEN
      ASSERT button.className CONTAINS "active"
    END IF
  END FOR
END FOR
```

**Pseudocode (Bug 3a):**
```
FOR ALL sessions WHERE actual StudentExam count > 0 DO
  result := GET /api/Sessions
  sessionDto := result.find(s => s.id == session.id)
  ASSERT sessionDto.studentCount == actual StudentExam count
END FOR
```

**Pseudocode (Bug 3b):**
```
FOR ALL executions OF startExamProcess WHERE
  first heartbeat response has isStarted == true DO
  
  ASSERT heartbeatInterval IS assigned BEFORE clearInterval() is called
  ASSERT clearInterval() cancels the correct interval
  ASSERT window.location.href = 'exam.html' is called EXACTLY ONCE
END FOR
```

---

### Preservation Checking

**Goal**: Verify that for all inputs where each bug condition does NOT hold, the fixed code produces the same result as the original code.

**Pseudocode (Bug 1):**
```
FOR ALL subjectConfigs WHERE NOT isBugCondition_1(subjectConfigs) DO
  ASSERT fixedSave(subjectConfigs) == originalSave(subjectConfigs)
  ASSERT fixedDropdown(row) == originalDropdown(row) MINUS alreadySelectedSubjects
END FOR
```

**Pseudocode (Bug 2):**
```
FOR ALL calls TO renderSubjectTabs() DO
  ASSERT fixedActiveTab.className == originalActiveTab.className
  ASSERT fixedInactiveTab.className == originalInactiveTab.className
  ASSERT switchSubject() behavior is identical
END FOR
```

**Pseudocode (Bug 3):**
```
FOR ALL sessions WHERE NOT isBugCondition_3a(session) DO
  ASSERT fixedGetAll(session).studentCount == originalGetAll(session).studentCount
END FOR

FOR ALL executions WHERE heartbeat returns isStarted == false DO
  ASSERT fixedWaitingRoom behavior == originalWaitingRoom behavior
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because it generates many test cases automatically across the input domain, catches edge cases that manual unit tests might miss, and provides strong guarantees that behavior is unchanged for all non-buggy inputs.

**Test Cases:**
1. **Bug 1 — Valid Template Save Preserved**: Create a template with unique subjects [English, Mathematics, Physics] → save succeeds as before.
2. **Bug 1 — Year Loading Preserved**: Select a subject in a row → years load from question bank as before.
3. **Bug 2 — Tab Switch Preserved**: Click each subject tab → question view switches to that subject as before.
4. **Bug 2 — Active Class Toggle Preserved**: Active tab has `active` class; inactive tabs do not.
5. **Bug 3 — Pre-Start Polling Preserved**: Student arrives before exam starts → polling continues, no redirect fires.
6. **Bug 3 — GetStudents Endpoint Preserved**: `GET /api/Sessions/{id}/students` continues to return correct student data.

---

### Unit Tests

- Test that `ExamSubjectConfigVM.AvailableSubjects` excludes subjects already selected in sibling rows.
- Test that `SaveTemplateAsync` returns an error when duplicate subjects are present.
- Test that `SaveTemplateAsync` succeeds when all subjects are unique.
- Test that `renderSubjectTabs()` produces buttons with no inline `style` attributes.
- Test that `.btn-tab` and `.btn-tab.active` CSS rules produce the correct computed styles.
- Test that `SessionsController.GetAll()` returns the correct `studentCount` when students have joined.
- Test that `pollHeartbeatSignal` does not trigger multiple redirects when `isStarted` is true on the first call.

### Property-Based Tests

- Generate random combinations of subject selections across 1–4 rows and verify that the fixed system never allows two rows to share the same subject.
- Generate random sets of subjects and verify that each row's `AvailableSubjects` is always the global list minus the subjects selected in other rows.
- Generate random sequences of heartbeat responses (mix of `isStarted: false` and `isStarted: true`) and verify that exactly one redirect fires and the interval is always cleared.
- Generate random session states with varying student counts and verify that `GetAll()` always returns the correct count.

### Integration Tests

- Full template builder flow: add 4 subjects, attempt to set a duplicate, verify rejection, then save a valid template.
- Full exam page flow: load exam with 3 subjects, switch between tabs, verify CSS-only styling and correct question filtering.
- Full waiting room flow: student joins after exam already started → single redirect to `exam.html` fires after countdown.
- Admin session dashboard: start session, have students join, verify session card shows correct student count.
