##### FileSync

Utility will synchronize files between specified source and destination directories. Utility will save html report and send mail report.

## Usage
filesync.exe "source1" "destination1" ... "sourceN" "destinationN"

## Config
- BufferSize: how many bytes are read and written in single FileStream call
- FileCopyRetryCount: how many times single file is tried to be copied when errors occur
- HtmlReportOutputFile: path where HTML report is saved
- MailTimeoutMilliseconds: SmtpClient timeout
- MailSubject: mail subject text
- MailRecipients: mail recipients separated with semicolon, ie "foo@bar.net;bar@baz.io"
- SMTP config: https://msdn.microsoft.com/en-us/library/w355a94k%28v=vs.110%29.aspx

## Build
.\build.ps1