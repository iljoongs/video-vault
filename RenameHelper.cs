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
    /// "파일 이동"(속성 창)/"선택한 파일 이동"(메인 창)이 "코드" 이름의 하위 폴더를 만들 기준 폴더(2026-08-16 추가).
    /// 예전에는 파일이 지금 있는 폴더의 바로 위 폴더를 기준으로 삼았는데, 실제 라이브러리에는 파일이 여러 단계
    /// 깊이 폴더에 들어있는 경우(`0_jp`, `0_Actresses\배우이름`, `fc2` 등)가 많아 그 규칙대로면 엉뚱한 위치에
    /// "코드" 폴더가 생기곤 했다. 실제 라이브러리 구조(`E:\happy\{코드}\{파일}`)에 맞춰 항상 이 경로를 기준으로
    /// 삼도록 고정했다 — `ThumbnailHelper.PlaceholderThumbnailDir`와 같은 패턴(이 PC의 실제 경로를 상수로 고정).
    /// </summary>
    public const string LibraryBasePath = @"E:\happy";

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

        // 기록된 위치(item.FullPath)에 파일이 이미 없는 경우(관리 밖에서 옮겨지거나 지워진 항목, "파일 없음"으로
        // 표시되는 상태) — 옮길 원본 자체가 없으므로, 사용자가 고른 위치를 "이동 대상"이 아니라 "이 항목의 실제
        // 파일 위치"로 그대로 재연결한다(2026-08-16 추가, 사용자가 다른 위치의 같은 파일로 재연결을 시도하다가
        // 실패하는 것을 보고 발견). **이 분기가 없으면 두 가지 문제가 있었다**: ① 원본이 없는데 `File.Move`를
        // 시도해 `FileNotFoundException`으로 그냥 실패했다. ② 더 심각하게는, 아래의 "대상이 있으면 먼저 지운다"
        // 로직이 이 경우에도 먼저 실행돼서, 사용자가 재연결하려던 그 대상 파일 자체를 지워버린 뒤에야 이동이
        // 실패하는 — 재연결하려던 파일까지 잃는 데이터 유실 버그가 될 뻔했다. 그래서 원본 존재 여부를 가장 먼저
        // 확인해 완전히 다른 경로(이동 없이 경로만 갱신)로 처리한다.
        if (!File.Exists(item.FullPath))
        {
            if (!File.Exists(newFullPath))
            {
                MessageBox.Show(
                    $"기존 파일과 선택한 파일 모두 존재하지 않습니다.\n기존 경로: {item.FullPath}\n선택한 경로: {newFullPath}",
                    "경로 수정 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            item.FullPath = newFullPath;
            item.FileName = Path.GetFileName(newFullPath);

            var healNameNoExt = Path.GetFileNameWithoutExtension(newFullPath);
            RenameAssociatedFile(item, newDirectory, healNameNoExt, isThumbnail: true);
            RenameAssociatedFile(item, newDirectory, healNameNoExt, isThumbnail: false);

            ActorCreditSync.OnFileRenamed(item, oldFileName, masterActors);
            SeriesCreditSync.OnFileRenamed(item, oldFileName, masterSeries);

            return true;
        }

        // SaveFileDialog의 OverwritePrompt가 이미 "이 파일을 바꾸시겠습니까?" 확인을 거쳤으므로, 대상 파일이 있으면
        // 직접 지운 뒤 옮긴다(2026-08-16 수정) — 예전에는 File.Move(..., overwrite: true)의 내장 덮어쓰기에 맡겼는데,
        // 대상 파일이 다른 프로그램(재생 중인 플레이어 등)에서 열려 있거나 읽기 전용이면 그 덮어쓰기 자체가 조용히
        // 실패하면서 "지정된 파일이 이미 있으므로 파일을 만들 수 없습니다" 같은 알아보기 힘든 원본 예외 메시지가
        // 그대로 떠서, 사용자에게는 마치 (방금 확인까지 했는데) "중복된 파일이 있어서 실패한다"처럼 보이는 문제가
        // 있었다. 지우는 단계를 따로 떼어내 실패 원인(주로 파일 사용 중/읽기 전용)을 명확히 짚어주는 메시지로 바꿨다.
        if (File.Exists(newFullPath))
        {
            try
            {
                File.Delete(newFullPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"대상 위치에 이미 있는 파일을 지울 수 없어 경로를 수정할 수 없습니다.\n" +
                    "다른 프로그램(재생 중인 플레이어 등)에서 그 파일을 열어두고 있거나 읽기 전용일 수 있습니다.\n\n" +
                    $"대상 경로: {newFullPath}\n{ex.Message}",
                    "경로 수정 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        try
        {
            File.Move(item.FullPath, newFullPath);
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
    /// 실패 시 <see cref="MessageBox"/>를 직접 띄운다 — 단일 항목을 다루는 [속성 관리](properties-management.md)의
    /// "파일 이동"/"경로 수정"처럼 그때그때 사용자에게 바로 알려도 괜찮은 곳에서 쓴다. **여러 항목을 한 번에 처리하는
    /// 곳(`MainWindow.MoveSelectedFiles_Click`)에서는 이 메서드 대신 <see cref="TryMoveToSpecificFolderSilent"/>를
    /// 써야 한다** — 안 그러면 항목마다 대화상자가 떠서 여러 개를 한꺼번에 옮길 때(예: 이름이 겹치는 항목이 많은 경우)
    /// 대화상자가 줄줄이 쌓여 사실상 응답 없음처럼 보이는 문제가 있었다(2026-08-16 실제 발견·수정).
    /// </summary>
    public static bool TryMoveToSpecificFolder(ManagedVideoItem item, string newDirectory)
    {
        var succeeded = TryMoveToSpecificFolderSilent(item, newDirectory, out var failureReason, out var exception, out var alreadyInPlace);

        // 이미 같은 위치면(할 일 없음) 예전과 동일하게 아무 메시지 없이 false를 돌려준다 — 호출자(예: "경로 수정"에서
        // 지금 폴더를 다시 고른 경우)가 "아무 변화 없음"으로 조용히 처리하던 것과 동일하게 유지하기 위함이다.
        if (alreadyInPlace)
        {
            return false;
        }

        if (!succeeded)
        {
            if (failureReason is not null)
            {
                MessageBox.Show(failureReason, "폴더 이동 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (exception is not null)
            {
                MessageBox.Show($"폴더로 이동할 수 없습니다.\n{exception.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// <see cref="TryMoveToSpecificFolder"/>와 동일하게 이동하지만 대화상자를 전혀 띄우지 않는다(2026-08-16 추가) —
    /// 실패하면 <paramref name="failureReason"/>(이미 같은 이름의 파일이 있는 경우 등 예상 가능한 실패, 사용자에게
    /// 보여줄 문구)나 <paramref name="exception"/>(그 외 `File.Move` 등에서 발생한 예외) 중 하나를 채워 반환한다.
    /// 이미 같은 위치인 경우(할 일 없음)는 실패로 취급하지 않고 그냥 `true`를 반환한다 — 호출자가 "이동함"으로
    /// 잘못 보고하지 않도록 <paramref name="alreadyInPlace"/>로 구분해서 알려준다.
    /// </summary>
    public static bool TryMoveToSpecificFolderSilent(ManagedVideoItem item, string newDirectory,
        out string? failureReason, out Exception? exception, out bool alreadyInPlace)
    {
        failureReason = null;
        exception = null;
        alreadyInPlace = false;

        var newFullPath = Path.Combine(newDirectory, item.FileName);

        if (string.Equals(newFullPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            alreadyInPlace = true;
            return true;
        }

        if (File.Exists(newFullPath))
        {
            failureReason = "같은 이름의 파일이 대상 폴더에 이미 존재합니다.";
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
            exception = ex;
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
    /// "movie2.srt"처럼 이름이 우연히 접두사만 겹치는 파일은 제외한다.
    /// <para>
    /// 위 정확한 일치 규칙에 걸리지 않아도, "품번 코드"(영문 접두사 + 숫자, 예: "avop-208")가 같으면 "비슷한
    /// 이름"으로 보고 함께 옮긴다(2026-08-16 추가 — 실사용 중 하이픈 누락/앞뒤 부가 텍스트가 붙은 자막이 다수
    /// 발견되어 요청받음). 예: `avop00208.srt`(하이픈 없음, 앞자리 0), `dandy-317-slut.smi`(뒤에 부가 텍스트),
    /// `DV-1357 Tsukasa Aoi.smi`(대소문자·부가 텍스트), `[HD-자막] CJOD-350 쿠로카와 사리나.srt`(앞뒤 부가 텍스트)
    /// 모두 각각 `avop-208`/`dandy-317`/`dv-1357`/`cjod-350` 코드와 일치해서 자동으로 인식된다 — <see cref="ExtractCode"/>
    /// 참고. 이 경우는 붙어있던 부가 텍스트를 딱히 보존할 구조가 없으므로(언어 코드처럼 "."으로 구분되지 않음)
    /// 새 품번 이름만으로 새로 짓는다(부가 텍스트는 버림). 코드를 추출할 수 없는 순수 숫자/한글 파일명(예: "172.smi",
    /// "코가와 이오리.smi")은 대상에서 제외된다(무엇과도 매칭하지 않아 안전).
    /// </para>
    /// 썸네일/원본 파일과 마찬가지로 부가 정리이므로, 실패해도(대상 이름 충돌 등) 동영상 파일 rename/이동
    /// 자체는 되돌리지 않는다.
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

        var oldCode = ExtractCode(oldNameNoExt);

        foreach (var oldPath in filesInFolder)
        {
            var extension = Path.GetExtension(oldPath);
            if (!SubtitleExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var nameNoExt = Path.GetFileNameWithoutExtension(oldPath);
            string newPath;

            var isExactPrefixMatch = nameNoExt.StartsWith(oldNameNoExt, StringComparison.OrdinalIgnoreCase) &&
                                      (nameNoExt.Length == oldNameNoExt.Length || nameNoExt[oldNameNoExt.Length] == '.');

            if (isExactPrefixMatch)
            {
                var suffix = nameNoExt[oldNameNoExt.Length..];
                newPath = Path.Combine(newDirectory, $"{newNameNoExt}{suffix}{extension}");
            }
            else if (oldCode is not null && oldCode == ExtractCode(nameNoExt))
            {
                newPath = Path.Combine(newDirectory, $"{newNameNoExt}{extension}");
            }
            else
            {
                continue;
            }

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

    /// <summary>
    /// 파일명에서 "품번 코드"(영문 접두사 + 숫자)를 뽑아 정규화한다 — 하이픈 유무, 숫자 앞자리 0, 대소문자,
    /// 앞뒤에 붙은 부가 텍스트(배우 이름, "[HD-자막]" 같은 태그 등)를 무시하고 비교할 수 있게 한다.
    /// 문자열 어디에서든 "영문자+(하이픈)+숫자" 패턴 중 첫 번째로 찾은 것을 코드로 쓴다(예: "avop-208",
    /// "avop00208", "[HD-자막] CJOD-350 이름"에서 모두 "cjod-208"/"avop-208"/"cjod-350"을 뽑아낼 수 있음).
    /// 이런 패턴이 전혀 없는 순수 숫자/한글 파일명은 <see langword="null"/>을 반환해 매칭 대상에서 제외한다.
    /// </summary>
    private static string? ExtractCode(string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(name, @"[a-zA-Z]+-?0*\d+");
        if (!match.Success)
        {
            return null;
        }

        var letters = System.Text.RegularExpressions.Regex.Match(match.Value, @"[a-zA-Z]+").Value.ToLowerInvariant();
        var digits = System.Text.RegularExpressions.Regex.Match(match.Value, @"\d+").Value.TrimStart('0');
        if (digits.Length == 0)
        {
            digits = "0";
        }

        return $"{letters}-{digits}";
    }
}
