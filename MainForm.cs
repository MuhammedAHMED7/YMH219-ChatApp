namespace ChatApp;

public sealed class MainForm : Form
{
    public MainForm()
    {
        Text = "ChatApp";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
    }
}
