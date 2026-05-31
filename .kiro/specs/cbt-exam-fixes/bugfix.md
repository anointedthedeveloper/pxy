# Bugfix Requirements Document

## Introduction

This document covers three related bugs in the WPF + ASP.NET Core CBT (Computer-Based Testing) exam system. The bugs affect three distinct areas: (1) the ASAM exam template builder allows duplicate subjects to be selected across rows, leading to invalid exam configurations; (2) the subject tab buttons on the exam page use inline JavaScript styles that override CSS, causing visual inconsistency and layout problems; and (3) the student waiting room has a race condition where students who arrive after an exam is already started may get stuck, and the admin session card always shows a student count of zero because the related `StudentExams` collection is never loaded from the database.

---

## Bug Analysis

### Current Behavior (Defect)

**Issue 1 — Duplicate Subject Prevention in ASAM Template Builder**

1.1 WHEN a user manually selects a subject in a row's ComboBox that is already selected in another row THEN the system allows the duplicate selection without any validation or error message.

1.2 WHEN a user opens the subject ComboBox in any row THEN the system shows all available subjects including those already selected in other rows, providing no visual indication of which subjects are already in use.

1.3 WHEN a user clicks "Create Template" or "Update Template" with duplicate subjects across rows THEN the system does not validate for duplicate subjects before saving, allowing an invalid exam template to be persisted.

**Issue 2 — Subject Tab Buttons Inline Style Override**

1.4 WHEN the exam page renders subject tabs via `renderSubjectTabs()` in `app.js` THEN the system applies inline `style` properties directly on each button element (padding, borderRadius, fontWeight, background, color, border, boxShadow), overriding any CSS class-based styling.

1.5 WHEN the exam page is viewed on a smaller screen or the subject tabs bar contains many subjects THEN the system renders tabs with inconsistent styling and the layout does not wrap or display polished visual feedback because inline styles conflict with the responsive CSS.

**Issue 3 — Waiting Room Student Count and Redirect Race Condition**

1.6 WHEN the admin views the session cards in the Sessions dashboard THEN the system always displays a student count of zero because `SessionsController.GetAll()` queries `db.ExamSessions` without `.Include(s => s.StudentExams)`, causing `s.StudentExams.Count` to always evaluate to zero.

1.7 WHEN a student arrives at the waiting room after the exam has already been started (`isStarted = true`) THEN the system proceeds through the full join and question-download flow and then starts the heartbeat polling loop, but the `starting` flag and `heartbeatInterval` variable may not be initialized before the first heartbeat fires, creating a race condition where the redirect to `exam.html` may not trigger reliably.

1.8 WHEN the heartbeat endpoint returns `isStarted: true` and a valid `decryptionKey` THEN the system sets `starting = true` and clears `heartbeatInterval`, but if `heartbeatInterval` has not yet been assigned (because the first heartbeat call fires synchronously before the `setInterval` assignment), `clearInterval(heartbeatInterval)` is called with `undefined`, leaving the interval running and potentially triggering multiple concurrent redirects.

---

### Expected Behavior (Correct)

**Issue 1 — Duplicate Subject Prevention in ASAM Template Builder**

2.1 WHEN a user selects a subject in a row's ComboBox that is already selected in another row THEN the system SHALL reject the selection, revert the ComboBox to its previous value, and display a clear error message indicating the subject is already in use.

2.2 WHEN a user opens the subject ComboBox in any row THEN the system SHALL display only subjects that are not already selected in other rows, dynamically filtering the available options so duplicates cannot be chosen.

2.3 WHEN a user clicks "Create Template" or "Update Template" THEN the system SHALL validate that no two subject rows share the same subject name before saving, and if duplicates are found it SHALL display an error message and prevent the save operation.

**Issue 2 — Subject Tab Buttons Inline Style Override**

2.4 WHEN the exam page renders subject tabs via `renderSubjectTabs()` THEN the system SHALL apply only CSS class names (`btn-tab` and `btn-tab active`) to each button element, with no inline `style` properties set by JavaScript.

2.5 WHEN the exam page is viewed on any screen size THEN the system SHALL render subject tabs using CSS-defined styles for `.btn-tab` and `.btn-tab.active` in `exam.css`, ensuring consistent appearance, proper wrapping, and visual polish that matches the rest of the exam UI.

**Issue 3 — Waiting Room Student Count and Redirect Race Condition**

2.6 WHEN the admin views the session cards in the Sessions dashboard THEN the system SHALL display the accurate count of students who have joined each session by including `.Include(s => s.StudentExams)` in the `SessionsController.GetAll()` query so that `s.StudentExams.Count` reflects the real number of joined students.

2.7 WHEN a student arrives at the waiting room and the active-session endpoint returns `isStarted: true` on the very first poll THEN the system SHALL skip the heartbeat polling loop entirely and proceed directly to the decryption and countdown flow, avoiding the race condition.

2.8 WHEN the heartbeat polling loop is started THEN the system SHALL ensure the `heartbeatInterval` variable is assigned before the first heartbeat call can complete, so that `clearInterval(heartbeatInterval)` always cancels the correct interval and no duplicate redirects occur.

---

### Unchanged Behavior (Regression Prevention)

**Issue 1 — Duplicate Subject Prevention**

3.1 WHEN a user selects a subject in a row's ComboBox that is not already used by any other row THEN the system SHALL CONTINUE TO accept the selection and load the available years for that subject as before.

3.2 WHEN a user uses the "Add Subject" batch context menu to add multiple subjects at once THEN the system SHALL CONTINUE TO skip subjects already present in the list (existing `BatchAddSubjectsCommand` guard) and add only new, non-duplicate subjects.

3.3 WHEN a user saves a valid exam template with no duplicate subjects and all required fields filled THEN the system SHALL CONTINUE TO create or update the template successfully.

**Issue 2 — Subject Tab Buttons**

3.4 WHEN the exam page renders subject tabs THEN the system SHALL CONTINUE TO highlight the active subject tab differently from inactive tabs, and clicking a tab SHALL CONTINUE TO switch the question view to that subject.

3.5 WHEN the exam page is used on a desktop screen THEN the system SHALL CONTINUE TO display all subject tabs in a single row without overflow, and the tabs SHALL CONTINUE TO be clickable and functional.

**Issue 3 — Waiting Room and Student Count**

3.6 WHEN a student arrives at the waiting room before the exam has been started THEN the system SHALL CONTINUE TO poll the active-session endpoint, join the session, download encrypted questions, and wait for the heartbeat to signal `isStarted: true` before redirecting.

3.7 WHEN the heartbeat returns `isStarted: false` THEN the system SHALL CONTINUE TO remain on the waiting room page and continue polling without triggering any redirect.

3.8 WHEN the admin terminates a session THEN the system SHALL CONTINUE TO auto-submit unsubmitted student exams and export results as before.

3.9 WHEN the admin views the session audit log (history table) THEN the system SHALL CONTINUE TO display all past sessions with their recorded student counts and statuses.
