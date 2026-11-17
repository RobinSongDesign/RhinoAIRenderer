using System.ComponentModel;
using System.Windows.Data;

namespace AIRenderer.Services
{
    /// <summary>
    /// 可绑定的本地化代理。在 XAML 中声明为 StaticResource，
    /// 通过索引器 {Binding [Key], Source={StaticResource L}} 使用。
    /// 语言切换时自动通知所有绑定刷新，无需重启。
    /// </summary>
    public class LocalizationManager : INotifyPropertyChanged
    {
        public LocalizationManager()
        {
            Loc.LanguageChanged += () =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        }

        public string this[string key] => Loc.Get(key);

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
