using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using VendorSync.Core;

namespace VendorSync.UI;

/// <summary>
/// XAML 本地化标记扩展：Text="{loc:LocExt Home.AddProject}"。
/// 返回一个绑定到 Loc.LocSink 索引器的 Binding；语言切换时 Loc 触发
/// "Item[]" 的 PropertyChanged，所有在用绑定自动刷新（等同 DynamicResource 的效果）。
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExt : MarkupExtension
{
    private readonly string _key;

    public LocExt(string key) => _key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding("Item[" + _key + "]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay
        };
        // 让 Binding 自己针对目标属性取值（Window.Title 等 CLR/DP 目标都能正确挂上）。
        return binding.ProvideValue(serviceProvider);
    }
}
