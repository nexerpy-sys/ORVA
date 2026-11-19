using System.Windows.Controls;

namespace Orva.Views
{
    public partial class ModuleNoticeView : UserControl
    {
        public ModuleNoticeView(string message)
            : this(message, null)
        {
        }

        public ModuleNoticeView(string message, string title)
        {
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(title))
            {
                TitleText.Text = title;
            }
            NoticeText.Text = message ?? string.Empty;
        }
    }
}