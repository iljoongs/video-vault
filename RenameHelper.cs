using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace VideoVault;

/// <summary>
/// 관리 리스트 항목이 가리키는 실제 동영상 파일의 이름을 변경한다 (디스크의 파일 자체를 rename).
/// </summary>
public static class RenameHelper
{
    /// <summary>
    /// 항목의 썸네일/원본 파일을 <paramref name="newDirectory"/>로 옮기고 파일명을 <paramref name="newNameNoExt"/>
    /// 기준으로 다시 붙인다(2026-08-07 추가). rename/이동이 아니라, "제거된 데이터 재사용"으로 항목의 실제
    /// 파일 위치가 바뀔 때 기존 썸네일(예: "파일 없이 추가"의 고정 폴더 `E:\happy\thumbnail`에 있던 것, 또는
    /// 예전 파일 위치에 남아있던 것)을 새 동영상 파일과 같은 폴더로 따라오게 하기 위해 쓴다
    /// (`ManagedListImporter.AddFiles`). 대상 파일이 없거나 이름 충돌 등으로 실패해도 무시한다(부가 정리).
    /// </summary>
    public static void MoveThumbnailsToFolder(ManagedVideoItem item, string newDirectory, string newNameNoExt)
    {
        RenameAssociatedFile(item, newDirectory, newNameNoExt, isThumbnail: true);
        RenameAssociatedFile(item, newDirectory, newNameNoExt, isThumbnail: false);
    }

    public static bool TryRenameManagedItem(Window owner, ManagedVideoItem item, IEnumerable<ActorItem> masterActors, IEnumerable<SeriesItem> masterSeries)
    {
        var dialog = new RenameWindow(item.FileName) { Owner = owner };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        return TryRenameManagedItemTo(item, dialog.NewFileName, masterActors, masterSeries);
    }

    /// <summary>
    /// 대화상자 없이 지정된 새 파일명(같은 폴더 내)으로 즉시 rename한다. 유효성 검사(파일명 문자/중복)를 포함한다.
    /// `PropertiesWindow`의 파일명 텍스트 상자처럼, 별도 대화상자 없이 바로 적용해야 하는 곳에서 사용한다.
    /// </summary>
    public static bool TryRenameManagedItemTo(ManagedVideoItem item, string newFileName, IEnumerable<ActorItem> masterActors, IEnumerable<SeriesItem> masterSeries)
    {
        if (string.IsNullOrWhiteSpace(newFileName) || newFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show("올바른 파일명을 입력하세요.", "이름 변경 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var directory = Path.GetDirectoryName(item.FullPath);
        if (directory is null)
        {
            return false;
        }

        var newFullPath = Path.Combine(directory, newFileName);

        if (string.Equals(newFullPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (File.Exists(newFullPath))
        {
            MessageBox.Show("같은 이름의 파일이 이미 존재합니다.", "이름 변경 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var oldFileName = item.FileName;

        try
        {
            File.Move(item.FullPath, newFullPath);
            item.FullPath = newFullPath;
            item.FileName = newFileName;

            var newNameNoExt = Path.GetFileNameWithoutExtension(newFileName);
            RenameAssociatedFile(item, directory, newNameNoExt, isThumbnail: true);
            RenameAssociatedFile(item, directory, newNameNoExt, isThumbnail: false);
            RenameAssociatedSubtitles(directory, Path.GetFileNameWithoutExtension(oldFileName), directory, newNameNoExt);

            ActorCreditSync.OnFileRenamed(item, oldFileName, masterActors);
            SeriesCreditSync.OnFileRenamed(item, oldFileName, masterSeries);

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"이름을 변경할 수 없습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// 관리 리스트 항목이 가리키는 실제 동영상 파일을 파일 대화상자로 고른 새 전체 경로로 이동한다
    /// (폴더/파일명 모두 변경 가능). 대상이 이미 존재하면 대화상자 자체의 덮어쓰기 확인을 거친다.
    /// </summary>
    public static bool TryEditFullPath(Window owner, ManagedVideoItem item, IEnumerable<ActorItem> masterActors, IEnumerable<SeriesItem> masterSeries)
    {
        var dialog = new SaveFileDialog
        {
            Title = "파일 경로 수정",
            FileName = item.FileName,
            InitialDirectory = Path.GetDirectoryName(item.FullPath),
            Filter = "모든 파일 (*.*)|*.*",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        var newFullPath = dialog.FileName;

        if (string.Equals(newFullPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var newDirectory = Path.GetDirectoryName(newFullPath);
        if (string.IsNullOrEmpty(newDirectory))
        {
            return false;
        }

        var oldFileName = item.FileName;
        var oldDirectory = Path.GetDirectoryName(item.FullPath);

        try
        {
            File.Move(item.FullPath, newFullPath, overwrite: true);
            item.FullPath = newFullPath;
            item.FileName = Path.GetFileName(newFullPath);

            var newNameNoExt = Path.GetFileNameWithoutExtension(newFullPath);
            RenameAssociatedFile(item, newDirectory, newNameNoExt, isThumbnail: true);
            RenameAssociatedFile(item, newDirectory, newNameNoExt, isThumbnail: false);
            if (oldDirectory is not null)
            {
                RenameAssociatedSubtitles(oldDirectory, Path.GetFileNameWithoutExtension(oldFileName), newDirectory, newNameNoExt);
            }

            ActorCreditSync.OnFileRenamed(item, oldFileName, masterActors);
            SeriesCreditSync.OnFileRenamed(item, oldFileName, masterSeries);

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"경로를 수정할 수 없습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// 관리 리스트 항목이 가리키는 실제 동영상 파일(및 관련 썸네일/원본 파일)을 폴더 선택 대화상자로 고른
    /// 새 폴더로 옮긴다. 파일명은 그대로 유지하고 폴더만 바뀐다 (`TryEditFullPath`는 파일명까지 바꿀 수 있는
    /// 반면, 이 메서드는 "같은 이름으로 폴더만 이동"하는 더 단순한 시나리오를 위한 것).
    /// </summary>
    public static bool TryMoveToFolder(Window owner, ManagedVideoItem item)
    {
        var dialog = new OpenFolderDialog { Title = "이동할 폴더 선택" };
        if (dialog.ShowDialog(owner) != true)
        {
            return false;
        }

        return TryMoveToSpecificFolder(item, dialog.FolderName);
    }

    /// <summary>
    /// 파일(및 관련 썸네일/원본 파일)을 <paramref name="newDirectory"/>로 옮긴다. 파일명은 그대로 유지한다.
    /// 대화상자를 열지 않고, 호출자가 이미 정한 대상 폴더로 바로 이동할 때 사용한다 (대상 폴더가 없으면 새로 만든다).
    /// </summary>
    public static bool TryMoveToSpecificFolder(ManagedVideoItem item, string newDirectory)
    {
        var newFullPath = Path.Combine(newDirectory, item.FileName);

        if (string.Equals(newFullPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (File.Exists(newFullPath))
        {
            MessageBox.Show("같은 이름의 파일이 대상 폴더에 이미 존재합니다.", "폴더 이동 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var oldDirectory = Path.GetDirectoryName(item.FullPath);

        try
        {
            Directory.CreateDirectory(newDirectory);
            File.Move(item.FullPath, newFullPath);
            item.FullPath = newFullPath;

            var newNameNoExt = Path.GetFileNameWithoutExtension(item.FileName);
            RenameAssociatedFile(item, newDirectory, newNameNoExt, isThumbnail: true);
            RenameAssociatedFile(item, newDirectory, newNameNoExt, isThumbnail: false);
            if (oldDirectory is not null)
            {
                RenameAssociatedSubtitles(oldDirectory, newNameNoExt, newDirectory, newNameNoExt);
            }

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"폴더로 이동할 수 없습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// 동영상 파일이 rename/이동되면 "{예전 이름}.thumbnail.jpg" / "{예전 이름}.original{확장자}" 파일도
    /// 새 이름·새 폴더(<paramref name="newDirectory"/>, 동영상의 새 위치)에 맞춰 함께 옮긴다. 대상 파일이 없거나
    /// 이름 충돌 등으로 실패해도 방금 성공한 동영상 파일 rename/이동 자체는 되돌리지 않는다 (부가적인 정리일 뿐이므로).
    /// </summary>
    private static void RenameAssociatedFile(ManagedVideoItem item, string newDirectory, string newNameNoExt, bool isThumbnail)
    {
        var currentPath = isThumbnail ? item.ThumbnailPath : item.ThumbnailOriginalPath;
        if (currentPath is null || !File.Exists(currentPath))
        {
            return;
        }

        var extension = Path.GetExtension(currentPath);
        var suffix = isThumbnail ? ".thumbnail" : ".original";
        var newPath = Path.Combine(newDirectory, $"{newNameNoExt}{suffix}{extension}");

        if (string.Equals(currentPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            File.Move(currentPath, newPath);

            if (isThumbnail)
            {
                item.ThumbnailPath = newPath;
            }
            else
            {
                item.ThumbnailOriginalPath = newPath;
            }
        }
        catch
        {
            // 썸네일/원본 파일 이름 변경은 부가 기능이므로, 실패해도(대상 이름 충돌 등) 동영상 파일 이름 변경 자체는 유지한다.
        }
    }

    private static readonly string[] SubtitleExtensions = { ".srt", ".smi", ".ass", ".ssa", ".vtt", ".sub" };

    /// <summary>
    /// 동영상과 같은 폴더에 있는 자막 파일을 동영상 rename/이동에 맞춰 함께 옮긴다(2026-08-05 추가). 파일명이
    /// 동영상 이름과 정확히 같거나("movie.srt") 그 뒤에 언어 코드 등이 "."으로 이어지는 경우("movie.kor.srt")까지
    /// 자막으로 인식하며, 그 나머지 부분(언어 코드 + 확장자)은 그대로 유지한 채 동영상 이름 부분만 바꾼다.
    /// "movie2.srt"처럼 이름이 우연히 접두사만 겹치는 파일은 제외한다. 썸네일/원본 파일과 마찬가지로 부가
    /// 정리이므로, 실패해도(대상 이름 충돌 등) 동영상 파일 rename/이동 자체는 되돌리지 않는다.
    /// </summary>
    private static void RenameAssociatedSubtitles(string oldDirectory, string oldNameNoExt, string newDirectory, string newNameNoExt)
    {
        if (!Directory.Exists(oldDirectory))
        {
            return;
        }

        IEnumerable<string> filesInFolder;
        try
        {
            filesInFolder = Directory.EnumerateFiles(oldDirectory).ToList();
        }
        catch
        {
            return;
        }

        foreach (var oldPath in filesInFolder)
        {
            var extension = Path.GetExtension(oldPath);
            if (!SubtitleExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var nameNoExt = Path.GetFileNameWithoutExtension(oldPath);
            if (!nameNoExt.StartsWith(oldNameNoExt, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (nameNoExt.Length > oldNameNoExt.Length && nameNoExt[oldNameNoExt.Length] != '.')
            {
                continue;
            }

            var suffix = nameNoExt[oldNameNoExt.Length..];
            var newPath = Path.Combine(newDirectory, $"{newNameNoExt}{suffix}{extension}");

            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) || File.Exists(newPath))
            {
                continue;
            }

            try
            {
                File.Move(oldPath, newPath);
            }
            catch
            {
                // 자막 파일 이름 변경은 부가 기능이므로, 실패해도(대상 이름 충돌 등) 동영상 파일 이름 변경 자체는 유지한다.
            }
        }
    }
}
