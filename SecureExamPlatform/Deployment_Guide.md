# Secure Exam Platform - Deployment & Usage Guide

## 🎯 Quick Start Guide

### For Faculty/Administrators

#### Step 1: Build and Deploy the Application

1. **Build the Solution**
   ```bash
   dotnet build SecureExamPlatform.sln --configuration Release
   ```

2. **Publish for Deployment**
   ```bash
   dotnet publish SecureExamPlatform/SecureExamPlatform.csproj -c Release -r win-x64 --self-contained false
   ```

3. **Deploy to Lab Computers**
   - Copy the published files to each exam computer
   - Ensure .NET 8.0 Runtime is installed
   - The application requires Administrator privileges (specified in app.manifest)

#### Step 2: Import Exam Questions

1. **Launch the Application** (it will open the Student Login by default)

2. **Optional: Open Faculty Dashboard** 
   - To access the Faculty Dashboard, you need to modify the code temporarily:
   - In `App.xaml.cs`, change the OnStartup method to show FacultyDashboard instead of LoginWindow
   - Or create a separate executable for faculty use

3. **Prepare Your Exam JSON File**
   ```json
   {
     "examId": "EXAM-001",
     "title": "Your Exam Title",
     "duration": 90,
     "questions": [
       {
         "id": "q1",
         "text": "Question text?",
         "type": "MCQ",
         "options": ["Option A", "Option B", "Option C", "Option D"],
         "correctAnswer": "Option A",
         "marks": 2
       },
       {
         "id": "q2",
         "text": "Essay question?",
         "type": "Subjective",
         "marks": 10
       }
     ]
   }
   ```

4. **Import the Exam**
   - Click "📋 Import Questions"
   - Select your JSON file
   - Click "⬆ Import Questions"
   - The exam will be saved to the `Exams` folder

#### Step 3: Generate Student Credentials

1. **For Each Student-Computer Pair:**
   - Click "🔑 Generate Credentials"
   - Select the exam from dropdown
   - Enter Student ID (e.g., "STU001")
   - Enter Hardware ID (get from student's computer)
   - Enter Computer Name (optional)
   - Click "Generate"

2. **Get Hardware ID from Student Computer:**
   - Run this PowerShell command on the student's machine:
   ```powershell
   Get-WmiObject Win32_ComputerSystemProduct | Select-Object -ExpandProperty UUID
   ```
   - Or use the CredentialGenerator tool

3. **Give Student:**
   - Student ID
   - Access Token
   - TOTP Secret (for their authenticator app)

### For Students

#### Step 1: Setup Authenticator App (One-Time)

1. **Install an Authenticator App:**
   - Google Authenticator (Android/iOS)
   - Microsoft Authenticator (Android/iOS/Windows)
   - Any TOTP-compatible app

2. **Add the Exam Account:**
   - Open your authenticator app
   - Add a new account (usually a + button)
   - Choose "Enter key manually"
   - Enter the TOTP Secret provided by your instructor
   - Account name: Your Student ID
   - Save it

#### Step 2: Login to Exam

1. **Launch the Exam Application**
   - The application will open automatically on exam computers
   - Do NOT close it during the exam

2. **Enter Your Credentials:**
   - Student ID: (provided by instructor)
   - Access Token: (provided by instructor)
   - Press Enter or click "Start Exam"

3. **Enter TOTP Code:**
   - Open your authenticator app
   - Find the 6-digit code for this exam
   - Enter it quickly (codes change every 30 seconds)
   - Click "Start Exam"

4. **Take the Exam:**
   - Answer questions in any order
   - Use navigation panel to jump between questions
   - Flag questions for review (🚩 button)
   - Your answers auto-save every 30 seconds
   - Watch the timer in the top-right

5. **Submit:**
   - Click "Submit Exam" when done
   - Confirm your submission
   - The application will close automatically

## 🔧 System Architecture

### Directory Structure

```
SecureExamPlatform/
├── Exams/                      # Imported exam JSON files
├── %LocalAppData%/SecureExam/
│   ├── exam_credentials.json   # Generated credentials
│   ├── Submissions/            # Submitted exams
│   ├── Logs/                   # Security logs
│   └── session.lock            # Active session file
```

### Key Components

1. **LoginWindow**: Student authentication interface
2. **ExamWindow**: Secure exam taking environment
3. **FacultyDashboard**: Exam management and credential generation
4. **CredentialManager**: Handles credential generation and validation
5. **ExamSessionManager**: Manages exam sessions and loads exam content
6. **Security Components**: 
   - ProcessMonitor: Blocks unauthorized apps
   - ScreenshotPrevention: Prevents screen capture
   - TotpManager: 2FA authentication

## 📋 Exam JSON Format

### Supported Question Types

1. **Multiple Choice Questions (MCQ)**
   ```json
   {
     "id": "q1",
     "text": "Question text?",
     "type": "MCQ",
     "options": ["A", "B", "C", "D"],
     "correctAnswer": "A",
     "marks": 2
   }
   ```

2. **Subjective/Essay Questions**
   ```json
   {
     "id": "q2",
     "text": "Explain...",
     "type": "Subjective",
     "marks": 10
   }
   ```

### Full Exam Structure

```json
{
  "examId": "UNIQUE-ID",           // Required: Unique identifier
  "title": "Exam Title",           // Required: Display name
  "duration": 90,                  // Required: Minutes
  "instructions": "Instructions",  // Optional
  "questions": [...]               // Required: Array of questions
}
```

## 📊 Grading Submitted Exams

### Submission File Format

Submissions are saved as JSON files in:
`%LocalAppData%/SecureExam/Submissions/submission_{StudentID}_{ExamID}_{Timestamp}.json`

```json
{
  "sessionId": "guid",
  "studentId": "STU001",
  "examId": "EXAM-001",
  "answers": {
    "q1": "0",           // MCQ: index of selected option
    "q2": "Essay text"   // Subjective: full text answer
  },
  "submissionTime": "2025-11-07T10:30:00",
  "timeSpent": "01:25:00"
}
```

### Grading MCQ Questions

For MCQ questions, the answer is stored as an index (0-based):
- "0" = First option
- "1" = Second option
- etc.

Compare with the `correctAnswer` field in your exam JSON:

```csharp
// Example grading logic
var exam = LoadExam(submission.ExamId);
var question = exam.Questions.Find(q => q.Id == "q1");

if (question.Type == QuestionType.MultipleChoice)
{
    int selectedIndex = int.Parse(submission.Answers["q1"]);
    string selectedAnswer = question.Options[selectedIndex];
    bool isCorrect = selectedAnswer == question.CorrectAnswer;
    // Award marks if correct
}
```

### Creating a Grading Tool

```csharp
// Simple grading example
foreach (var submissionFile in Directory.GetFiles(submissionsPath, "*.json"))
{
    var submission = JsonSerializer.Deserialize<ExamSubmission>(
        File.ReadAllText(submissionFile));
    
    var exam = LoadExam(submission.ExamId);
    int totalScore = 0;
    
    foreach (var question in exam.Questions)
    {
        if (question.Type == QuestionType.MultipleChoice)
        {
            if (submission.Answers.TryGetValue(question.Id, out string answer))
            {
                int selectedIndex = int.Parse(answer);
                if (question.Options[selectedIndex] == question.CorrectAnswer)
                {
                    totalScore += question.Points;
                }
            }
        }
        // Subjective questions need manual grading
    }
    
    Console.WriteLine($"{submission.StudentId}: {totalScore} marks");
}
```

## 🔒 Security Features

### Active Security Mechanisms

1. **Hardware-Bound Credentials**: Can only be used on the specific computer
2. **Single-Use Tokens**: Credentials invalidate after first use
3. **Time-Limited**: Credentials expire after set duration
4. **2FA with TOTP**: Time-based one-time passwords
5. **Process Monitoring**: Blocks unauthorized applications
6. **Screenshot Prevention**: Prevents screen capture
7. **Keyboard Hooks**: Blocks Alt+Tab, Windows key, etc.
8. **Session Management**: Prevents multiple concurrent logins

### Security Logs

All security events are logged to:
`security_log_{Date}.txt`

Events include:
- Blocked applications
- Blocked keyboard shortcuts
- Suspicious activity
- Session start/end

## 🚨 Troubleshooting

### Student Can't Login

**Problem**: "Invalid credentials or none found"
- **Solution**: Ensure credentials were generated on the correct computer
- Check that exam_credentials.json exists in %LocalAppData%/SecureExam/
- Regenerate credentials if needed

**Problem**: "Hardware mismatch"
- **Solution**: Credentials are for a different computer
- Generate new credentials for this specific machine

**Problem**: "Credentials have expired"
- **Solution**: Generate fresh credentials (default: 180 minutes validity)

### Exam Won't Load

**Problem**: "Exam not found"
- **Solution**: Check that exam JSON file exists in the Exams folder
- Verify the examId in credentials matches the exam file
- Ensure JSON is properly formatted

### Application Crashes on Startup

**Problem**: Access Denied errors
- **Solution**: Run as Administrator (app.manifest requires admin)

**Problem**: .NET Runtime errors
- **Solution**: Install .NET 8.0 Runtime

## 💡 Best Practices

### For Exam Administrators

1. **Test Everything First**: Run a complete test with dummy credentials
2. **Backup Submissions**: Regularly backup the Submissions folder
3. **Monitor Logs**: Check security logs after each exam
4. **Have a Backup Plan**: Keep paper exams ready just in case
5. **Generate Credentials Early**: Create credentials 1-2 days before exam
6. **Verify Hardware IDs**: Double-check hardware IDs before generating

### For Creating Exams

1. **Clear Questions**: Write unambiguous questions
2. **Correct MCQ Format**: Ensure options array matches correctAnswer exactly
3. **Reasonable Duration**: Set appropriate time limits
4. **Mix Question Types**: Combine MCQ and subjective questions
5. **Test Your JSON**: Validate JSON before importing
6. **Use Unique IDs**: Ensure all question IDs are unique

### For Students

1. **Setup Early**: Configure authenticator app before exam day
2. **Check Battery**: Ensure phone/device is charged
3. **Save Frequently**: Although auto-save works, manually save important answers
4. **Read Instructions**: Check exam instructions before starting
5. **Manage Time**: Keep an eye on the timer
6. **Don't Close**: Never close the exam window until submitted

## 📞 Support

### Common Error Messages

| Error | Cause | Solution |
|-------|-------|----------|
| "Session expired" | TOTP took too long | Restart login process |
| "Maximum attempts exceeded" | Too many wrong TOTP codes | Contact administrator |
| "Hardware mismatch" | Wrong computer | Use assigned computer |
| "Exam not found" | Missing exam file | Contact administrator |

### Contact

For technical issues:
- Check logs in %LocalAppData%/SecureExam/Logs/
- Review security_log_{date}.txt
- Contact your IT department

## 🔄 Updates & Maintenance

### Regular Maintenance

1. **Clear Old Logs**: Archive logs older than 30 days
2. **Backup Submissions**: Backup submitted exams regularly
3. **Update Credentials**: Remove expired credentials periodically
4. **Test Security**: Regularly test security features

### Before Each Exam

- [ ] Test application on all computers
- [ ] Import exam questions
- [ ] Generate student credentials
- [ ] Verify hardware IDs
- [ ] Test one complete login flow
- [ ] Clear previous session files

### After Each Exam

- [ ] Collect submission files
- [ ] Review security logs
- [ ] Back up all data
- [ ] Generate grade reports
- [ ] Archive exam materials

---

**Version**: 1.0
**Last Updated**: November 2025
**Platform**: Windows 10/11, .NET 8.0