' CalendarExportModule for Outlook (64-bit Office)
' Exports calendar data to JSON for WinCalendar app
'
' Put this in a STANDARD MODULE (Insert -> Module)
' To start: Alt+F8 -> StartCalendarExport
' To stop:  Alt+F8 -> StopCalendarExport

Option Explicit

' === CONFIGURATION ===
Public Const EXPORT_INTERVAL_MS As Long = 30000
Public Const LOOKAHEAD_MINUTES As Integer = 5760
Public Const OUTPUT_FOLDER As String = ".config\win-calendar"

' === WINDOWS API (64-bit) ===
Private Declare PtrSafe Function SetTimer Lib "user32" (ByVal hwnd As LongPtr, ByVal nIDEvent As LongPtr, ByVal uElapse As Long, ByVal lpTimerFunc As LongPtr) As LongPtr
Private Declare PtrSafe Function KillTimer Lib "user32" (ByVal hwnd As LongPtr, ByVal nIDEvent As LongPtr) As Long
Private Declare PtrSafe Function GetSystemMetrics Lib "user32" (ByVal nIndex As Long) As Long
Private gTimerID As LongPtr

' === START THE TIMER ===
Public Sub StartCalendarExport()
    If gTimerID <> 0 Then
        KillTimer 0, gTimerID
    End If

    DoExport
    gTimerID = SetTimer(0, 0, EXPORT_INTERVAL_MS, AddressOf TimerCallback)

    If gTimerID = 0 Then
        MsgBox "Failed to create timer!", vbCritical
    Else
        ShowStatus "Calendar export started - " & (EXPORT_INTERVAL_MS / 1000) & "s interval"
    End If
End Sub

' === STOP THE TIMER ===
Public Sub StopCalendarExport()
    If gTimerID <> 0 Then
        KillTimer 0, gTimerID
        gTimerID = 0
        ShowStatus "Calendar export stopped"
    Else
        ShowStatus "Calendar export was not running"
    End If
End Sub

' === SHOW STATUS (non-blocking, auto-dismiss) ===
Private pNotifyForm As Object
Private pNotifyTimerId As LongPtr

Private Sub ShowStatus(ByVal message As String)
    On Error Resume Next

    ' Close any existing notification
    CloseNotification

    ' Create a simple userform programmatically
    Set pNotifyForm = CreateObject("Forms.Form.1")
    With pNotifyForm
        .Caption = "WinCalendar"
        .Width = 350
        .Height = 80
        .StartUpPosition = 0 ' Manual

        ' Position in bottom-right of screen
        .Left = GetSystemMetrics(0) - .Width - 20  ' SM_CXSCREEN
        .Top = GetSystemMetrics(1) - .Height - 60  ' SM_CYSCREEN

        ' Add label
        Dim lbl As Object
        Set lbl = .Controls.Add("Forms.Label.1", "lblMessage")
        lbl.Caption = message
        lbl.Left = 10
        lbl.Top = 20
        lbl.Width = 320
        lbl.Height = 40
        lbl.Font.Size = 10
    End With

    ' Show modeless
    pNotifyForm.Show vbModeless

    ' Set timer to close after 3 seconds
    pNotifyTimerId = SetTimer(0, 0, 3000, AddressOf NotifyTimerCallback)

    Debug.Print Now & " - " & message
End Sub

Private Sub NotifyTimerCallback(ByVal hwnd As LongPtr, ByVal uMsg As Long, ByVal idEvent As LongPtr, ByVal dwTime As Long)
    On Error Resume Next
    CloseNotification
End Sub

Private Sub CloseNotification()
    On Error Resume Next
    If pNotifyTimerId <> 0 Then
        KillTimer 0, pNotifyTimerId
        pNotifyTimerId = 0
    End If
    If Not pNotifyForm Is Nothing Then
        pNotifyForm.Hide
        Set pNotifyForm = Nothing
    End If
End Sub

' === TIMER CALLBACK ===
Private Sub TimerCallback(ByVal hwnd As LongPtr, ByVal uMsg As Long, ByVal idEvent As LongPtr, ByVal dwTime As Long)
    On Error Resume Next
    DoExport
End Sub

' === EXPORT CALENDAR TO JSON ===
Public Sub DoExport()
    On Error GoTo ErrorHandler

    Dim olNs As Outlook.NameSpace
    Dim calFolder As Outlook.MAPIFolder
    Dim calItems As Outlook.Items
    Dim appt As Object
    Dim filteredItems As Outlook.Items

    Dim startTime As Date
    Dim endTime As Date
    Dim sFilter As String
    Dim json As String
    Dim itemJson As String
    Dim isFirst As Boolean
    Dim fso As Object
    Dim f As Object

    Set olNs = Outlook.Application.GetNamespace("MAPI")
    Set calFolder = olNs.GetDefaultFolder(olFolderCalendar)
    Set calItems = calFolder.Items

    calItems.Sort "[Start]"
    calItems.IncludeRecurrences = True

    startTime = Date ' Start of today (midnight)
    endTime = DateAdd("n", LOOKAHEAD_MINUTES, Now)

    sFilter = "[Start] >= '" & Format(startTime, "mm/dd/yyyy hh:mm AMPM") & "'" & _
              " AND [Start] <= '" & Format(endTime, "mm/dd/yyyy hh:mm AMPM") & "'"

    Set filteredItems = calItems.Restrict(sFilter)

    json = "{" & vbCrLf
    json = json & "  ""exportTime"": """ & Format(Now, "yyyy-mm-ddThh:nn:ss") & """," & vbCrLf
    json = json & "  ""events"": [" & vbCrLf

    isFirst = True
    For Each appt In filteredItems
        If TypeOf appt Is AppointmentItem Then
            If appt.MeetingStatus <> olMeetingCanceled And appt.BusyStatus <> olFree Then
                If Not isFirst Then json = json & "," & vbCrLf
                isFirst = False

                ' Count attendees
                Dim requiredCount As Integer
                Dim optionalCount As Integer
                Dim recip As Recipient
                requiredCount = 0
                optionalCount = 0
                For Each recip In appt.Recipients
                    If recip.Type = olRequired Then
                        requiredCount = requiredCount + 1
                    ElseIf recip.Type = olOptional Then
                        optionalCount = optionalCount + 1
                    End If
                Next recip

                itemJson = "    {" & vbCrLf
                itemJson = itemJson & "      ""subject"": """ & JsonEscape(appt.Subject) & """," & vbCrLf
                itemJson = itemJson & "      ""start"": """ & Format(appt.Start, "yyyy-mm-ddThh:nn:ss") & """," & vbCrLf
                itemJson = itemJson & "      ""end"": """ & Format(appt.End, "yyyy-mm-ddThh:nn:ss") & """," & vbCrLf
                itemJson = itemJson & "      ""location"": """ & JsonEscape(appt.Location) & """," & vbCrLf
                itemJson = itemJson & "      ""organizer"": """ & JsonEscape(appt.Organizer) & """," & vbCrLf
                itemJson = itemJson & "      ""requiredAttendees"": " & requiredCount & "," & vbCrLf
                itemJson = itemJson & "      ""optionalAttendees"": " & optionalCount & "," & vbCrLf
                itemJson = itemJson & "      ""entryId"": """ & appt.EntryID & """" & vbCrLf
                itemJson = itemJson & "    }"
                json = json & itemJson
            End If
        End If
    Next appt

    json = json & vbCrLf & "  ]" & vbCrLf & "}"

    Set fso = CreateObject("Scripting.FileSystemObject")
    Set f = fso.CreateTextFile(GetOutputPath(), True, True)
    f.Write json
    f.Close

    Exit Sub

ErrorHandler:
    Debug.Print "Export error: " & Err.Description
End Sub

' === HELPER FUNCTIONS ===
Public Function GetOutputPath() As String
    Dim fso As Object
    Dim folderPath As String
    Set fso = CreateObject("Scripting.FileSystemObject")
    folderPath = Environ("USERPROFILE") & "\" & OUTPUT_FOLDER
    If Not fso.FolderExists(folderPath) Then
        fso.CreateFolder folderPath
    End If
    GetOutputPath = folderPath & "\calendar-data.json"
End Function

Private Function JsonEscape(ByVal s As String) As String
    s = Replace(s, "\", "\\")
    s = Replace(s, """", "\""")
    s = Replace(s, vbCr, "")
    s = Replace(s, vbLf, " ")
    s = Replace(s, vbTab, " ")
    JsonEscape = s
End Function
