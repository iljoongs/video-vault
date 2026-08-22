using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace VideoVault;

/// <summary>
/// 동영상 파일들을 관리 리스트에 추가하는 공용 로직. 폴더 목록 창의 "관리 리스트에 추가"와 관리
/// 리스트 영역으로의 드래그 앤 드롭이 공유한다(둘 다 여러 파일을 한 번에 넘길 수 있다). 이미 활성
/// 상태로 관리 중인 경로는 건너뛰고(전부 처리가 끝난 뒤 건너뛴 파일명을 목록으로 한 번에 보여줌),
/// 같은 파일명의 제거된 데이터가 있으면 재사용 여부를 물어본다 — [제거(Remove) 메커니즘] 참고.
/// </summary>
public static class ManagedListImporter
{
    public static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg"
    };

    public static void AddFiles(ObservableCollection<ManagedVideoItem> managedItems, IEnumerable<VideoFileItem> files)
    {
        var activePaths = managedItems
            .Where(m => m.IsValid)
            .Select(m => m.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var reused = 0;
        var alreadyManaged = new List<string>();

        foreach (var selected in files)
        {
            if (activePaths.Contains(selected.FullPath))
            {
                alreadyManaged.Add(selected.FileName);
                continue;
            }

            // 제거된 데이터는 파일명이 완전히 같을 때만 매칭한다. 단, "파일 없이 추가"로 만든 항목
            // (IsPlaceholder)은 실제 파일의 확장자를 알 수 없어 확장자 없이 입력된 경우가 많으므로,
            // 그 경우에는 확장자를 뺀 이름으로도 매칭한다(대소문자 무시) — 예: "sdmu-102" ↔ "sdmu-102.mp4".
            var archivedMatch = managedItems.FirstOrDefault(m =>
                !m.IsValid &&
                (string.Equals(m.FileName, selected.FileName, StringComparison.Ordinal) ||
                 (m.IsPlaceholder && string.Equals(m.FileName, Path.GetFileNameWithoutExtension(selected.FileName), StringComparison.OrdinalIgnoreCase))));

            if (archivedMatch is not null)
            {
                var result = MessageBox.Show(
                    $"'{selected.FileName}' 파일에 대해 이전에 관리하던 데이터가 있습니다.\n" +
                    $"(재생횟수 {archivedMatch.PlayCount}, 태그 {archivedMatch.Tags.Count}개)\n\n" +
                    "기존 데이터를 재사용하시겠습니까?\n예 = 기존 데이터 재사용(경로만 새로 갱신), 아니요 = 새 항목으로 추가",
                    "기존 데이터 발견",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // 재사용 전에 썸네일이 있었다면(예: "파일 없이 추가"의 고정 폴더에 있던 것, 또는 예전
                    // 파일 위치에 남아있던 것) 새 동영상 파일과 같은 폴더로 옮긴다 — FullPath를 갱신하기
                    // 전의 "예전" 폴더/이름이 아니라 지금(재사용 확정 시점) 실제 저장돼 있는 경로 기준으로
                    // 옮겨야 하므로 FullPath 갱신 직후, 아래에서 바로 처리한다.
                    archivedMatch.FileName = selected.FileName;
                    archivedMatch.FullPath = selected.FullPath;
                    archivedMatch.SizeBytes = selected.SizeBytes;
                    archivedMatch.ModifiedDate = selected.ModifiedDate;
                    archivedMatch.IsValid = true;
                    archivedMatch.IsExist = true;
                    archivedMatch.IsPlaceholder = false;

                    var newDirectory = Path.GetDirectoryName(selected.FullPath);
                    if (newDirectory is not null)
                    {
                        RenameHelper.MoveThumbnailsToFolder(archivedMatch, newDirectory, Path.GetFileNameWithoutExtension(selected.FileName));
                    }

                    activePaths.Add(selected.FullPath);
                    reused++;
                    continue;
                }
            }

            managedItems.Add(ManagedVideoItem.FromFolderItem(selected));
            activePaths.Add(selected.FullPath);
            added++;
        }

        if (alreadyManaged.Count > 0)
        {
            MessageBox.Show(
                $"다음 {alreadyManaged.Count}개 파일은 이미 관리 리스트에 있어 건너뛰었습니다.\n\n{string.Join("\n", alreadyManaged)}",
                "이미 추가된 파일",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else if (added == 0 && reused == 0)
        {
            MessageBox.Show("추가할 새 파일이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
