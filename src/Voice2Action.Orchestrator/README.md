# Orchestrator using tools exposed with MCP

## Sample Run

```
Connecting to MCP servers: utility=http://localhost:5010 office=http://localhost:5020
Utility MCP tools: GetCurrentDateTime, TranscribeVoiceRecording
Office MCP tools: SetReminder, SendEmail
Enter path to audio file (or blank to exit):
./audio-samples/sample-recording-1-task-with-due-date-and-reminder.mp3
--- Orchestration Complete ---
Transcript: "Follow up with my boss. Latest by August 30th. Remind me August 20th. We should talk about our AI strategy."
Summary: Reminder set for task 'Follow up with my boss about our AI strategy' due on 2023-08-30 with a reminder on 2023-08-20.
Actions:
  Planner -> Plan: {"Action":"DELEGATE","Agent":"UtilityAgent","Task":"TranscribeVoiceRecording(audioPath='/home/kai/src/agent-framework-voice2action/audio-samples/sample-recordin
  UtilityAgent -> Transcribe: "Follow up with my boss. Latest by August 30th. Remind me August 20th. We should talk about our AI strategy."
  Planner -> Plan: {"Action":"DELEGATE","Agent":"OfficeAutomation","Task":"SetReminder: task=\"Follow up with my boss about our AI strategy\" due=2023-08-30 reminder=2023-08-20","
  OfficeAutomation -> SetReminder: Reminder set for task 'Follow up with my boss about our AI strategy' due at 8/30/2023 12:00:00 AM. Reminder will trigger at 8/20/2023 12:00:00 AM.
  Planner -> Plan: {"Action":"DONE","Agent":"","Task":"","Summary":"Reminder set for task 'Follow up with my boss about our AI strategy' due on 2023-08-30 with a reminder on 2023-
```