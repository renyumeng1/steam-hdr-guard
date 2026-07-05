$ErrorActionPreference = "Stop"

if (Get-ScheduledTask -TaskName "Steam HDR Guard" -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName "Steam HDR Guard" -Confirm:$false
    Write-Host "已删除计划任务：Steam HDR Guard"
} else {
    Write-Host "计划任务不存在：Steam HDR Guard"
}
