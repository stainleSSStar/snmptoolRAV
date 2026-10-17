using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SnmptoolRAV {
    sealed class V3Dialog : Form {
        readonly TextBox user=Box("SNMPv3 username",false);
        readonly TextBox context=Box("SNMPv3 context name",false);
        readonly ComboBox level=Choice("Security level");
        readonly ComboBox auth=Choice("Authentication algorithm");
        readonly TextBox authPassword=Box("Authentication passphrase",true);
        readonly ComboBox privacy=Choice("Privacy algorithm");
        readonly TextBox privacyPassword=Box("Privacy passphrase",true);
        readonly CheckBox reveal=new CheckBox{Text="Show passphrases",AutoSize=true,ForeColor=Theme.Muted};
        public V3Profile Profile {get;private set;}
        static TextBox Box(string name,bool secret){return new TextBox{AccessibleName=name,Dock=DockStyle.Top,Font=new Font("Segoe UI",11),UseSystemPasswordChar=secret,MaxLength=secret?512:32,Margin=new Padding(0,3,16,0)};}
        static ComboBox Choice(string name){return new ComboBox{AccessibleName=name,Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,Font=new Font("Segoe UI",10),FlatStyle=FlatStyle.Flat,BackColor=Theme.Surface,Margin=new Padding(0,3,16,0)};}
        static Label Label(string text,float size,bool bold){return new Label{Text=text,Dock=DockStyle.Fill,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=bold?Theme.Text:Theme.Muted,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,Margin=new Padding(0)};}
        static void Field(TableLayoutPanel panel,int col,int row,string label,Control input){panel.Controls.Add(Label(label,9,true),col,row);panel.Controls.Add(input,col,row+1);}
        public V3Dialog(V3Profile original){
            Text="snmptoolRAV - SNMPv3 profile";Font=new Font("Segoe UI",10);BackColor=Theme.Background;ForeColor=Theme.Text;
            AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(704,528);MinimumSize=new Size(680,560);StartPosition=FormStartPosition.CenterParent;MaximizeBox=false;MinimizeBox=false;
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(24)};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));root.RowStyles.Add(new RowStyle(SizeType.Absolute,38));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,58));root.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
            root.Controls.Add(Label("SNMPv3 profile",20,true),0,0);root.Controls.Add(Label("Match the user and security settings configured on your device.",10,false),0,1);
            var card=new Card{Dock=DockStyle.Fill,Padding=new Padding(18,12,4,12),Margin=new Padding(0,0,0,12)};
            var fields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=8,Margin=new Padding(0),BackColor=Theme.Surface};fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));for(int i=0;i<4;i++){fields.RowStyles.Add(new RowStyle(SizeType.Absolute,21));fields.RowStyles.Add(new RowStyle(SizeType.Absolute,35));}
            Field(fields,0,0,"Username",user);Field(fields,1,0,"Context name (optional)",context);
            Field(fields,0,2,"Security level",level);Field(fields,1,2,"Authentication",auth);
            Field(fields,0,4,"Authentication passphrase",authPassword);Field(fields,1,4,"Privacy / encryption",privacy);
            Field(fields,0,6,"Privacy passphrase",privacyPassword);fields.Controls.Add(reveal,1,7);card.Controls.Add(fields);root.Controls.Add(card,0,2);
            root.Controls.Add(Label("Passphrases need at least 8 UTF-8 bytes. Profiles stay in memory only.\r\nAES-128 is supported. SHA-1 is available for explicit legacy compatibility.",9,false),0,3);
            var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Margin=new Padding(0)};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));
            var cancel=new ActionButton("Cancel",false){Dock=DockStyle.Fill,DialogResult=DialogResult.Cancel,Margin=new Padding(0,0,12,0)};var apply=new ActionButton("Apply profile",true){Dock=DockStyle.Fill,Margin=new Padding(0)};actions.Controls.Add(cancel,1,0);actions.Controls.Add(apply,2,0);root.Controls.Add(actions,0,4);Controls.Add(root);AcceptButton=apply;CancelButton=cancel;
            level.Items.AddRange(new object[]{"noAuthNoPriv","authNoPriv","authPriv"});auth.Items.AddRange(new object[]{"SHA-256","SHA-384","SHA-512","SHA-1 (legacy)"});privacy.Items.Add("AES-128");privacy.SelectedIndex=0;
            var source=original??new V3Profile();user.Text=source.UserName;context.Text=source.Context;authPassword.Text=source.AuthPassword;privacyPassword.Text=source.PrivacyPassword;auth.SelectedIndex=(int)source.Authentication;level.SelectedIndex=(int)source.Level;
            level.SelectedIndexChanged+=delegate{UpdateFields();};reveal.CheckedChanged+=delegate{authPassword.UseSystemPasswordChar=privacyPassword.UseSystemPasswordChar=!reveal.Checked;};
            apply.Click+=delegate{try{var result=new V3Profile{UserName=user.Text,Context=context.Text,Level=(V3Level)level.SelectedIndex,Authentication=(V3Auth)auth.SelectedIndex,AuthPassword=level.SelectedIndex>0?authPassword.Text:"",PrivacyPassword=level.SelectedIndex==2?privacyPassword.Text:""};result.Validate();Profile=result;DialogResult=DialogResult.OK;Close();}catch(ArgumentException ex){MessageBox.Show(this,ex.Message,"SNMPv3 profile",MessageBoxButtons.OK,MessageBoxIcon.Information);}};
            UpdateFields();
        }
        void UpdateFields(){auth.Enabled=authPassword.Enabled=level.SelectedIndex>0;privacy.Enabled=privacyPassword.Enabled=level.SelectedIndex==2;reveal.Enabled=level.SelectedIndex>0;}
        internal static void Render(string directory){if(new V3Profile{UserName="operator",AuthPassword="sample-authentication",PrivacyPassword="sample-privacy"}.Provider()==null)throw new Exception("Embedded SNMP library did not load.");using(var form=new V3Dialog(new V3Profile{UserName="operator",AuthPassword="sample-authentication",PrivacyPassword="sample-privacy"})){form.Show();Application.DoEvents();Save(form,directory,"v3-profile");form.level.SelectedIndex=0;Application.DoEvents();Save(form,directory,"v3-noauth");if(form.authPassword.Enabled||form.privacyPassword.Enabled)throw new Exception("Unused v3 secrets remain enabled.");form.Hide();}}
        static void Save(Form form,string directory,string name){using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(Path.Combine(directory,name+".png"),System.Drawing.Imaging.ImageFormat.Png);}}
    }
}
