Option Explicit
Dim shell, files, basePath, processEnv, appPath
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")
basePath = files.GetParentFolderName(WScript.ScriptFullName)
Set processEnv = shell.Environment("PROCESS")
processEnv("DOTNET_ROOT") = basePath & "\runtime"
processEnv("DOTNET_ROOT_X64") = basePath & "\runtime"
appPath = basePath & "\CIE.exe"
shell.Run Chr(34) & appPath & Chr(34), 0, False
