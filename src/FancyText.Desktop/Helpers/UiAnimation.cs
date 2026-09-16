using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FancyText.Desktop.Helpers;

/// <summary>
/// 弹窗动效统一入口。约定（对齐轻量红线）：
/// 只动 Opacity 与 TranslateTransform，全部有限时长（无 Forever），不做布局属性/笔刷颜色动画；
/// 总闸 = 系统"动画"开关（<see cref="SystemParameters.ClientAreaAnimation"/>）——
/// 模板故事板在建样式时由调用方判断 <see cref="Enabled"/>（关掉则退化为瞬时 Setter），
/// 代码侧动画由本类助手内部判断（直接到终态）；
/// 代码侧动画一律 FillBehavior.Stop + 终态先写本地值——播完即摘时钟，隐藏后无活跃时钟。
/// </summary>
internal static class UiAnimation
{
    public const int HoverMs = 83;   // 悬停级反馈（对齐设计稿 83ms）
    public const int SelectMs = 167; // 选中/聚焦/淡入级反馈（对齐设计稿 167ms）
    public const int ShowMs = 200;   // 唤出位移（设计稿 cubic-bezier(.33,1,.68,1) ≈ CubicEase EaseOut）
    public const int HideMs = 150;   // 退出（与进入同款淡出+上移，退场更快一档——Fluent 惯例）

    /// <summary>用户「减少动效」开关：主窗在构建/应用设置时写入；false 时全部动效跳过。</summary>
    public static bool UserPreference { get; set; } = true;

    /// <summary>系统动画总闸叠加用户开关：任一关闭则所有动效跳过（模板动画退化为瞬时 Setter，代码侧直接到终态）。</summary>
    public static bool Enabled => UserPreference && SystemParameters.ClientAreaAnimation;

    /// <summary>共享的冻结缓动实例：可被多个动画引用（Freeze 后跨动画/故事板共享是安全的）。</summary>
    private static readonly CubicEase CubicOut = NewCubicOut();

    private static CubicEase NewCubicOut()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    // ---------- 动画工厂（模板故事板用；每次调用都是新实例，模板/样式密封时会被自动冻结） ----------
    // 注意：这里不能提前 Freeze——冻结后 OpacityAction 再挂 TargetName 会因写冻结对象抛 InvalidOperationException。

    /// <summary>83ms Opacity 渐变（hover）。</summary>
    public static DoubleAnimation Fade83(double to, FillBehavior fill = FillBehavior.HoldEnd) => Fade(to, HoverMs, fill: fill);

    /// <summary>167ms Opacity 渐变（选中/聚焦/空态淡入）。</summary>
    public static DoubleAnimation Fade167(double to, FillBehavior fill = FillBehavior.HoldEnd) => Fade(to, SelectMs, fill: fill);

    /// <summary>167ms 淡入（→1）。</summary>
    public static DoubleAnimation FadeIn167() => Fade167(1d);

    /// <summary>200ms 位移渐变（唤出，CubicEase EaseOut）。</summary>
    public static DoubleAnimation Rise200(double to) => Fade(to, ShowMs, CubicOut);

    private static DoubleAnimation Fade(double to, int ms, IEasingFunction? easing = null,
        FillBehavior fill = FillBehavior.HoldEnd)
    {
        // 运行期再闸一次：「减少动效」/系统动画关闭时 0ms 等价瞬时 Setter（模板故事板无需分叉）
        return new DoubleAnimation(to, TimeSpan.FromMilliseconds(Enabled ? ms : 0))
        {
            EasingFunction = easing,
            FillBehavior = fill,
        };
    }

    /// <summary>
    /// 模板触发器故事板：对命名元素播给定的冻结 Opacity 动画。
    /// Enter 传 <see cref="FillBehavior.HoldEnd"/>（停在终值，如选中态常亮）；
    /// Exit 传 <see cref="FillBehavior.Stop"/>（结束回本地值 0 并释放时钟）。
    /// </summary>
    public static BeginStoryboard OpacityAction(string targetName, DoubleAnimation animation)
    {
        Storyboard.SetTargetName(animation, targetName);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        return new BeginStoryboard { Storyboard = storyboard };
    }

    // ---------- 代码侧助手（Stop + 终态先写本地值，播完无挂钟） ----------

    /// <summary>
    /// Opacity 渐变到 to：终态先写本地值，动画 FillBehavior=Stop——播完时钟即弃，属性自然停在终值。
    /// 系统动画关闭时直接到终态；completed 无论如何都会回调（调用方时序不依赖动画是否真播）。
    /// </summary>
    public static void BeginOpacity(UIElement element, double to, int ms, Action? completed = null)
    {
        if (!Enabled || ms <= 0)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null); // 摘掉可能在播的旧时钟
            element.Opacity = to;
            completed?.Invoke();
            return;
        }

        var from = element.Opacity; // 读的是当前有效值（含在播动画），中途改向也平滑
        element.Opacity = to;
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { FillBehavior = FillBehavior.Stop };
        if (completed is null)
        {
            element.BeginAnimation(UIElement.OpacityProperty, animation);
            return;
        }

        var clock = animation.CreateClock();
        clock.Completed += (_, _) => completed();
        element.ApplyAnimationClock(UIElement.OpacityProperty, clock);
    }

    /// <summary>唤出位移：TranslateTransform.Y 渐变到 to（CubicEase EaseOut），同样 Stop + 本地值摘钟。</summary>
    public static void BeginTranslateY(TranslateTransform transform, double to, int ms)
    {
        if (!Enabled || ms <= 0)
        {
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = to;
            return;
        }

        var from = transform.Y;
        transform.Y = to;
        transform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = CubicOut,
                FillBehavior = FillBehavior.Stop,
            });
    }
}
