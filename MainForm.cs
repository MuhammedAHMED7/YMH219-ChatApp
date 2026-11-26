namespace ChatApp;

public sealed class MainForm : Form
{
    readonly TextBox host = new(){Text="127.0.0.1",Width=110}, nameBox = new(){Text="Misafir",Width=106,MaxLength=32};
    readonly NumericUpDown port = new(){Minimum=1,Maximum=65535,Value=5219,Width=78};
    readonly TextBox input = new(){Dock=DockStyle.Fill,MaxLength=2000,PlaceholderText="Mesajınızı yazın…"};
    readonly RichTextBox chat = new(){Dock=DockStyle.Fill,ReadOnly=true,BackColor=Color.White,BorderStyle=BorderStyle.None};
    readonly ListBox roster = new(){Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,HorizontalScrollbar=true};
    readonly Panel participants = new() { Dock = DockStyle.Right, Width = 224, Padding = new Padding(10), BackColor = Color.White, Visible = false };
    readonly Label status=StudioUi.Status();
    readonly Button connect,serve,leave,send;
    ChatClient? client;
    ChatServer? server;
    public MainForm()
    {
        StudioUi.Window(this,"Fırat Chat  /  Sohbet odası");
        connect=StudioUi.Button("Bağlan",async (_,_)=>await Join(false));
        serve=StudioUi.Button("Oda aç",async (_,_)=>await Join(true));
        leave=StudioUi.Button("Bağlantıyı kes",(_,_)=>CloseSession("Bağlantı kesildi."));
        send=StudioUi.Button("Gönder",async (_,_)=>await Send());
        var bar=StudioUi.Bar(55);
        bar.Controls.AddRange([StudioUi.Tag("Ad"),nameBox,StudioUi.Tag("Sunucu"),host,StudioUi.Tag("Port"),port]);
        var actions=StudioUi.Bar();actions.Controls.AddRange([serve,connect,leave]);
        participants.Controls.Add(roster);
        participants.Controls.Add(new Label{Text="ODADAKİ İSTEMCİLER",Dock=DockStyle.Top,Height=36,ForeColor=StudioUi.Accent});
        var compose=new Panel{Dock=DockStyle.Bottom,Height=54,Padding=new Padding(12,10,12,5)};
        send.Dock=DockStyle.Right; compose.Controls.Add(input); compose.Controls.Add(send);
        var body=new Panel{Dock=DockStyle.Fill,Padding=new Padding(16),BackColor=Color.White};body.Controls.Add(chat);
        Controls.AddRange([body,participants,compose,actions,bar,StudioUi.Header("Fırat Chat"),status]);
        input.KeyDown+=async (_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;await Send();}};
        Idle("Çevrimdışı  ·  Bir oda açın veya sunucuya bağlanın.");
    }
    void Idle(string message)
    {
        serve.Enabled=connect.Enabled=host.Enabled=nameBox.Enabled=port.Enabled=true;
        participants.Visible = false;
        leave.Enabled=send.Enabled=input.Enabled=false;status.Text=message;
    }
    void Ui(Action change)
    {
        if(!IsHandleCreated || IsDisposed) return;
        try { BeginInvoke(change); } catch(InvalidOperationException) {}
    }
    async Task Join(bool create)
    {
        serve.Enabled=connect.Enabled=host.Enabled=nameBox.Enabled=port.Enabled=false;
        status.Text="Bağlantı kuruluyor…"; leave.Enabled=true;
        var session=client=new ChatClient();
        try
        {
            if(create)
            {
                var room=server=new ChatServer((int)port.Value);
                room.Roster+=names=>Ui(()=>{if(room!=server)return;roster.Items.Clear();roster.Items.AddRange(names);});
                room.Notice+=s=>Ui(()=>{if(room==server)status.Text="Sunucu  ·  "+s;});
            }
            session.Received+=p=>Ui(()=>{if(session==client)Append(p,false);});
            session.Closed+=s=>Ui(()=>{if(session==client)CloseSession("Bağlantı kapandı  ·  "+s);});
            await session.Connect(create?"127.0.0.1":host.Text.Trim(),(int)port.Value,nameBox.Text);
            if(session!=client)return;
            send.Enabled=input.Enabled=true; input.Focus();
            status.Text=(create?"●  Sunucu açık":"●  Bağlı")+"   ·   "+nameBox.Text+"   ·   TCP "+port.Value;
            participants.Visible = create;
        }
        catch(Exception ex){if(client==session)CloseSession("Bağlanılamadı  ·  "+ex.Message);}
    }
    async Task Send()
    {
        if(client is not {} session || string.IsNullOrWhiteSpace(input.Text) || !send.Enabled)return;
        send.Enabled=false;
        var draft = input.Text;
        try
        {
            var packet = await session.Send(draft.Trim());
            if (client != session) return;
            Append(packet, true);
            if (input.Text == draft) input.Clear();
        }
        catch(Exception ex){if(client==session)CloseSession("Mesaj gönderilemedi  ·  "+ex.Message);}
        finally {if(client==session)send.Enabled=true;}
    }
    void Append(Packet p,bool mine)
    {
        chat.Select(chat.TextLength, 0);
        chat.SelectionAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        chat.SelectionColor=mine?StudioUi.Accent:Color.FromArgb(108,67,144);
        chat.SelectedText = $"{(mine ? "Siz" : p.Name)}    {p.Time.ToLocalTime():HH:mm:ss}\n";
        chat.SelectionColor=StudioUi.Ink;chat.SelectedText = p.Text + "\n\n";chat.ScrollToCaret();
    }
    void CloseSession(string why)
    {
        var old=client;client=null;old?.Dispose();
        var room=server;server=null;room?.Dispose();
        roster.Items.Clear();Idle(why);
    }
    protected override void OnFormClosed(FormClosedEventArgs e){CloseSession("");base.OnFormClosed(e);}
}