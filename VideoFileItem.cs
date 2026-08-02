using System.IO;

namespace VideoVault;

/// <summary>
/// 폴더 목록(임시 스캔 결과)의 항목 하나를 나타낸다. 그 자체로는 저장되지 않는다.
/// </summary>
public class VideoFileItem
{
    public VideoFileItem(FileInfo file)
    {
        FileName = file.Name;
        FullPath = file.FullName;
        SizeBytes = file.Length;
        ModifiedDate = file.LastWriteTime;
    }

    public string FileName { get; }
    public string FullPath { get; }
    public long SizeBytes { get; }
    public DateTime ModifiedDate { get; }

    public string SizeDisplay => FormatUtil.FormatSize(SizeBytes);
    public string ModifiedDisplay => ModifiedDate.ToString("yyyy-MM-dd HH:mm");
}
