using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using HollowMario.Setup;

namespace HollowMario.Launcher
{
    internal sealed class SetupWindow : Form
    {
        private readonly Options options;
        private readonly Preferences preferences;
        private readonly RadioButton community, remove;
        private readonly Label gameLabel, romLabel, status;
        private readonly Button play;
        private readonly Label romHeading;
        private readonly Button romChooser;
        private string game, rom;
        private bool busy;
        private bool rendering;
        protected override bool ShowWithoutActivation => rendering;
        internal SetupWindow(Options options)
        {
            this.options=options;
            preferences=Preferences.Load();
            game=options.Game ?? preferences.GameDirectory ?? Program.FindGame();
            rom=options.Render == null ? RomResolver.Find(options.RomDirectory,options.RomFile,preferences.RomPath,game) : null;
            int extra=!options.RomOnly&&!options.Melty?74:0;
            Text="Hollow Mario"; ClientSize=new Size(650,426+extra); FormBorderStyle=FormBorderStyle.FixedDialog;
            MaximizeBox=false; MinimizeBox=true; StartPosition=FormStartPosition.CenterScreen;
            BackColor=Color.FromArgb(247,248,250); Font=new Font("Segoe UI",10f); AutoScaleMode=AutoScaleMode.Dpi;
            Label title=LabelAt("Hollow Mario",28,25,590,42); title.Font=new Font("Segoe UI",24f,FontStyle.Bold);
            LabelAt(options.RomOnly ? "Choose your Super Mario 64 ROM to finish starting Mario." : "Play using the Hollow Knight community API.",30,80,590,30);
            community=Choice("Play Hollow Mario", "The community API is installed automatically if needed.",122);
            remove=Choice("Remove Hollow Mario", "Also removes older Hollow Mario installations.",196);
            remove.Visible=extra!=0;
            community.Checked=true;
            LabelAt("HOLLOW KNIGHT",30,217+extra,420,20).Font=new Font("Segoe UI",8f,FontStyle.Bold);
            gameLabel=LabelAt("",30,241+extra,460,26);
            ButtonAt("Choose game…",500,233+extra,120,34,ChooseGame);
            romHeading=LabelAt("SUPER MARIO 64 (US)",30,284+extra,420,20);romHeading.Font=new Font("Segoe UI",8f,FontStyle.Bold);
            romLabel=LabelAt("",30,308+extra,460,26);
            romChooser=ButtonAt("Choose ROM…",500,300+extra,120,34,ChooseRom);
            status=LabelAt("Your ROM stays on this PC.",30,366+extra,365,42); status.ForeColor=Color.FromArgb(84,91,103);
            play=ButtonAt(options.RomOnly ? "Continue" : "Play Hollow Mario",425,364+extra,195,40,Continue);
            play.BackColor=Color.FromArgb(35,87,183); play.ForeColor=Color.White; play.FlatStyle=FlatStyle.Flat; play.FlatAppearance.BorderSize=0;
            AcceptButton=play;
            if(options.RomOnly)
            {
                community.Visible=remove.Visible=false;
                foreach(Control control in Controls)
                    if(control.Top>=217 && control.Top<276) control.Visible=false;
            }
            RefreshLabels();
            remove.CheckedChanged+=(s,e)=>
            {
                romHeading.Visible=romLabel.Visible=romChooser.Visible=!remove.Checked;
                play.Text=remove.Checked?"Remove Hollow Mario":options.RomOnly?"Continue":"Play Hollow Mario";
                status.Text=remove.Checked?"Your game, saves and ROM will be kept.":"Your ROM stays on this PC.";
            };
            FormClosing+=(s,e)=> { if(busy)e.Cancel=true; };
        }
        private Label LabelAt(string text,int x,int y,int width,int height)
        { var label=new Label { Text=text,Location=new Point(x,y),Size=new Size(width,height),AutoEllipsis=true }; Controls.Add(label);return label; }
        private RadioButton Choice(string title,string subtitle,int y)
        {
            var radio=new RadioButton { Text=title+Environment.NewLine+subtitle,Location=new Point(30,y),Size=new Size(590,64),BackColor=Color.White,Padding=new Padding(14,0,8,0),AutoEllipsis=true };
            Controls.Add(radio);return radio;
        }
        private Button ButtonAt(string text,int x,int y,int width,int height,EventHandler click)
        { var button=new Button { Text=text,Location=new Point(x,y),Size=new Size(width,height),FlatStyle=FlatStyle.System };button.Click+=click;Controls.Add(button);return button; }
        private void RefreshLabels()
        {
            gameLabel.Text=string.IsNullOrEmpty(game)?"Choose your installed game":"Hollow Knight — ready to check";
            romLabel.Text=string.IsNullOrEmpty(rom)?"Choose your own unmodified US ROM":"ROM found — ready";
        }
        private void ChooseGame(object sender,EventArgs args)
        {
            using(var dialog=new OpenFileDialog { Title="Choose Hollow Knight",Filter="Hollow Knight|hollow_knight.exe",CheckFileExists=true })
                if(dialog.ShowDialog(this)==DialogResult.OK){game=Path.GetDirectoryName(dialog.FileName);RefreshLabels();}
        }
        private void ChooseRom(object sender,EventArgs args)
        {
            using(var dialog=new OpenFileDialog { Title="Choose your Super Mario 64 US ROM",Filter="Nintendo 64 ROM|*.z64;*.v64;*.n64|All files|*.*",CheckFileExists=true })
                if(dialog.ShowDialog(this)==DialogResult.OK)
                {
                    if(!RomResolver.Valid(dialog.FileName)) MessageBox.Show(this,"This build needs an unmodified Super Mario 64 US ROM. This file is not the supported version.","Hollow Mario",MessageBoxButtons.OK,MessageBoxIcon.Information);
                    else {rom=dialog.FileName;RefreshLabels();}
                }
        }
        private void Progress(string text)
        { if(IsHandleCreated)BeginInvoke(new Action(()=>status.Text=text)); }
        private async void Continue(object sender,EventArgs args)
        {
            if(busy)return;
            if(remove.Checked){await Remove();return;}
            if(!RomResolver.Valid(rom)){ChooseRom(sender,args);if(!RomResolver.Valid(rom))return;}
            if(!options.RomOnly && (string.IsNullOrEmpty(game)||!File.Exists(Path.Combine(game,"hollow_knight.exe"))))
            {ChooseGame(sender,args);if(string.IsNullOrEmpty(game))return;}
            try
            {
                if(!options.RomOnly)Program.RequireGameClosed();
                Busy(true);
                var mode=InstallMode.CommunityApi;
                preferences.GameDirectory=game;preferences.LastMode=mode;
                RomResolver.Remember(rom,preferences);
                if(options.RomOnly)
                {
                    if(options.NativeDirectory!=null)
                        await Task.Run(()=>NativeRuntime.Ensure(AppDomain.CurrentDomain.BaseDirectory,options.NativeDirectory,Progress));
                    if(options.Result!=null)JsonFile.Write(options.Result,preferences);
                    busy=false;Close();return;
                }
                string folder=await Task.Run(()=>
                {
                    var installer=new Installer(AppDomain.CurrentDomain.BaseDirectory);
                    Progress("Checking Hollow Mario…");installer.Verify();Program.RequireGameClosed();
                    Installer.CheckCommunity(game);
                    GameInstall.EnsureApi(game,Progress);Program.RequireGameClosed();
                    installer.PrepareCommunity(game,progress:Progress,preparedNative:options.NativeDirectory,requireGameClosed:Program.RequireGameClosed);return game;
                });
                preferences.ConfirmLoadingChoice(game,mode);
                if(!options.NoLaunch)Program.Launch(folder,rom,options.Melty);
                busy=false;Close();
            }
            catch(Exception error)
            {Busy(false);status.Text="Setup has paused.";MessageBox.Show(this,error.Message,"Hollow Mario",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        }
        private void Busy(bool value)
        {
            busy=value;
            foreach(Control control in Controls)
                if(control is Button||control is RadioButton)control.Enabled=!value;
        }
        private async Task Remove()
        {
            if(string.IsNullOrEmpty(game)||!File.Exists(Path.Combine(game,"hollow_knight.exe")))ChooseGame(this,EventArgs.Empty);
            if(string.IsNullOrEmpty(game)||!File.Exists(Path.Combine(game,"hollow_knight.exe")))return;
            try
            {
                Program.RequireGameClosed();Busy(true);status.Text="Checking Hollow Mario installations…";
                var uninstaller=new Uninstaller(game);
                await Task.Run(()=>uninstaller.Inspect());
                if(uninstaller.LocationsFound==0){status.Text="No Hollow Mario installation was found.";return;}
                Busy(false);
                if(MessageBox.Show(this,"Remove Hollow Mario from this game and its older installations, including disabled copies, its settings and cached downloads?"+
                    Environment.NewLine+Environment.NewLine+"Your original game, game saves, ROMs, community loader and other mods will be kept.",
                    "Remove Hollow Mario",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                Busy(true);
                await Task.Run(()=>uninstaller.Remove(Program.RequireGameClosed,Progress));
                preferences.LoadingChoiceConfirmed=false;
                status.Text="Hollow Mario removed. Your game, saves and ROM were kept.";
            }
            catch(Exception error)
            {status.Text="Removal has paused.";MessageBox.Show(this,error.Message,"Hollow Mario",MessageBoxButtons.OK,MessageBoxIcon.Information);}
            finally{Busy(false);}
        }
        internal void Render(string path)
        {
            rendering=true;ShowInTaskbar=false;Opacity=0;StartPosition=FormStartPosition.Manual;Location=new Point(-32000,-32000);
            Show();Application.DoEvents();
            using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(path,System.Drawing.Imaging.ImageFormat.Png);}
            Hide();
        }
    }
}
