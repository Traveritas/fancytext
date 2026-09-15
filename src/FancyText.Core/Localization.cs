using System.Globalization;

namespace FancyText.Core;

/// <summary>界面语言。</summary>
public enum AppLanguage
{
    Chinese,
    English,
}

/// <summary>语言偏好的解析（持久化值 "zh"/"en"/"auto"=跟随系统）与系统文化探测。</summary>
public static class Localization
{
    /// <summary>从系统 UI 文化解析：zh* → 中文，其余 → 英文。</summary>
    public static AppLanguage FromSystem() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? AppLanguage.Chinese : AppLanguage.English;

    /// <summary>
    /// 从持久化字符串解析："zh" → 中文，"en" → 英文，"auto" → 跟随系统；
    /// 无偏好（null/未知）默认中文——本产品以中文用户为主，且与本地化前的纯中文版本一致
    /// （注意部分中文系统的 Windows 显示语言列表英文优先，"跟随系统"会解析为英文，故不作为默认）。
    /// </summary>
    public static AppLanguage Resolve(string? stored) => stored switch
    {
        "en" => AppLanguage.English,
        "auto" => FromSystem(),
        _ => AppLanguage.Chinese,
    };
}

/// <summary>
/// UI 文案二选一助手，供代码构建 UI 的项目（桌面版 / CmdPal）共用：
/// 构建控件时求值，语言切换后由各自的 UI 重建链路重新求值。
/// </summary>
public static class Loc
{
    public static string S(AppLanguage lang, string zh, string en) =>
        lang == AppLanguage.English ? en : zh;
}
