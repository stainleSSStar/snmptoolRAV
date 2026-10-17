using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SnmptoolRAV {
    static class Program {
        [STAThread] static int Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--preview") { try { MainForm.Render(args[1]); return 0; } catch (Exception ex) { File.WriteAllText(args[1]+".error.txt",ex.ToString()); return 1; } }
            Application.Run(new MainForm(false)); return 0;
        }
    }
    public sealed class MainForm : Form {
        readonly ComboBox adapter = Choice("Network adapter");
        readonly TextBox range = Input("IPv4 subnet");
        readonly TextBox community = Input("SNMP community");
        readonly ComboBox version = Choice("SNMP version (optional)");
        readonly NumericUpDown port = Number("UDP port",1,65535,161);
        readonly NumericUpDown timeout = Number("Reply timeout in milliseconds",100,5000,600);
        readonly NumericUpDown retries = Number("Retries per version",0,2,0);
        readonly CheckBox show = new CheckBox { Text = "Show", AutoSize = true, ForeColor = Theme.Muted };
        readonly Label adapterInfo = TextLabel("Choose a configured IPv4 adapter. Virtual adapters are included.",9,false);
        readonly Label status = TextLabel("Choose an adapter and enter your community name to begin.",10,false);
        readonly Label selected = TextLabel("Select a discovered device first.",10,true);
        readonly ActionButton start = new ActionButton("Scan subnet",true);
        readonly ActionButton stop = new ActionButton("Stop",false);
        readonly ActionButton refresh = new ActionButton("Refresh adapters",false);
        readonly ActionButton read = new ActionButton("Read selected device",false);
        readonly ActionButton export = new ActionButton("Export CSV",false);
        readonly ActionButton readData = new ActionButton("Read data",true);
        readonly ComboBox preset = Choice("OID subtree preset");
        readonly TextBox oid = Input("Numeric OID subtree");
        readonly ProgressBar progress = new ProgressBar { Dock=DockStyle.Fill, Style=ProgressBarStyle.Continuous, Margin=new Padding(0) };
        readonly TabControl tabs = new TabControl { Dock=DockStyle.Fill, Font=new Font("Segoe UI",10) };
        readonly TabPage deviceTab = new TabPage("Devices (0)");
        readonly TabPage dataTab = new TabPage("Device data");
        readonly DataGridView devices = Grid();
        readonly DataGridView data = Grid();
        readonly TextBox value = new TextBox { ReadOnly=true, Multiline=true, ScrollBars=ScrollBars.Both, WordWrap=false, Dock=DockStyle.Fill, BorderStyle=BorderStyle.FixedSingle, Font=new Font("Segoe UI",10), AccessibleName="Selected SNMP value" };
        readonly ToolTip tips = new ToolTip { AutoPopDelay=15000 };
        readonly List<DeviceResult> results = new List<DeviceResult>();
        readonly List<DataRow> values = new List<DataRow>();
        readonly Card settings;
        readonly bool preview;
        CancellationTokenSource cancellation;
        bool busy,closing;
        string dataDevice="";
        static ComboBox Choice(string name) { return new ComboBox { AccessibleName=name, DropDownStyle=ComboBoxStyle.DropDownList, Dock=DockStyle.Top, Font=new Font("Segoe UI",10), BackColor=Theme.Surface, ForeColor=Theme.Text, FlatStyle=FlatStyle.Flat, Margin=new Padding(0,3,12,0), DropDownWidth=850 }; }
        static TextBox Input(string name) { return new TextBox { AccessibleName=name, Dock=DockStyle.Top, Font=new Font("Segoe UI",10), BorderStyle=BorderStyle.FixedSingle, Margin=new Padding(0,3,12,0) }; }
        static NumericUpDown Number(string name,int min,int max,int initial) { return new NumericUpDown { AccessibleName=name, Minimum=min, Maximum=max, Value=initial, Dock=DockStyle.Top, Font=new Font("Segoe UI",10), Margin=new Padding(0,3,12,0) }; }
        static Label TextLabel(string text,float size,bool bold) { return new Label { Text=text, Dock=DockStyle.Fill, ForeColor=bold?Theme.Text:Theme.Muted, Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular), TextAlign=ContentAlignment.MiddleLeft, AutoEllipsis=true, Margin=new Padding(0), UseMnemonic=false }; }
        static DataGridView Grid() {
            var grid=new DataGridView { Dock=DockStyle.Fill, ReadOnly=true, AllowUserToAddRows=false, AllowUserToDeleteRows=false, AllowUserToResizeRows=false, MultiSelect=false, SelectionMode=DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible=false, BackgroundColor=Theme.Surface, BorderStyle=BorderStyle.None, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill, EnableHeadersVisualStyles=false, CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal, GridColor=Theme.Border, AccessibleName="Results" };
            grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle { BackColor=Theme.Background, ForeColor=Theme.Text, Font=new Font("Segoe UI",9,FontStyle.Bold), Padding=new Padding(6) };
            grid.DefaultCellStyle=new DataGridViewCellStyle { BackColor=Theme.Surface, ForeColor=Theme.Text, SelectionBackColor=Theme.Contrast?SystemColors.Highlight:Color.FromArgb(227,236,255), SelectionForeColor=Theme.Contrast?SystemColors.HighlightText:Theme.Text, Font=new Font("Segoe UI",9), Padding=new Padding(6,0,6,0) };
            grid.RowTemplate.Height=34; grid.ColumnHeadersHeight=38; return grid;
        }
        static void Columns(DataGridView grid,params string[] names) { foreach (string name in names) grid.Columns.Add(new DataGridViewTextBoxColumn { Name=name, HeaderText=name, SortMode=DataGridViewColumnSortMode.Automatic }); }
        static void Field(TableLayoutPanel panel, int column,int row,string title,Control input) { panel.Controls.Add(TextLabel(title,9,true),column,row); panel.Controls.Add(input,column,row+1); }
        public MainForm(bool forPreview) {
            preview=forPreview; Text="snmptoolRAV - Network device discovery"; Font=new Font("Segoe UI",10); BackColor=Theme.Background; ForeColor=Theme.Text;
            Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(1080,720); MinimumSize=new Size(960,700); StartPosition=FormStartPosition.CenterScreen;
            var root=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=6, Padding=new Padding(22), BackColor=Theme.Background };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach (var style in new[] {new RowStyle(SizeType.Absolute,64),new RowStyle(SizeType.Absolute,224),new RowStyle(SizeType.Absolute,56),new RowStyle(SizeType.Percent,100),new RowStyle(SizeType.Absolute,8),new RowStyle(SizeType.Absolute,44)}) root.RowStyles.Add(style);
            var header=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, Margin=new Padding(0) }; header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));
            header.Controls.Add(TextLabel("snmptoolRAV",23,true),0,0); var tagline=TextLabel("PORTABLE  /  READ-ONLY SNMP",9,true); tagline.TextAlign=ContentAlignment.MiddleRight; header.Controls.Add(tagline,1,0); root.Controls.Add(header,0,0);
            settings=new Card { Dock=DockStyle.Fill, Margin=new Padding(0,0,0,12), Padding=new Padding(18,12,18,12) };
            var fields=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=6, RowCount=6, Margin=new Padding(0), BackColor=Theme.Surface };
            foreach (float width in new[] {28f,23f,17f,10f,12f,10f}) fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,width));
            foreach (int height in new[] {22,35,28,22,36,35}) fields.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            Field(fields,0,0,"Network adapter / IPv4 address",adapter); fields.SetColumnSpan(adapter,6); fields.SetColumnSpan(fields.GetControlFromPosition(0,0),6);
            fields.Controls.Add(adapterInfo,0,2); fields.SetColumnSpan(adapterInfo,6);
            Field(fields,0,3,"IPv4 subnet (CIDR)",range); Field(fields,1,3,"Community",community); Field(fields,2,3,"Version (optional)",version); Field(fields,3,3,"UDP port",port); Field(fields,4,3,"Timeout (ms)",timeout); Field(fields,5,3,"Retries",retries);
            fields.Controls.Add(show,1,5);
            var note=TextLabel("Auto: v2c then v1. Community stays in memory; v1/v2c send it unencrypted. SNMPv3 is planned.",8,false); fields.Controls.Add(note,2,5); fields.SetColumnSpan(note,4);
            community.UseSystemPasswordChar=true; show.CheckedChanged+=delegate {community.UseSystemPasswordChar=!show.Checked;}; version.Items.AddRange(new object[] {"Auto (v2c / v1)","SNMP v2c","SNMP v1"}); version.SelectedIndex=0;
            settings.Controls.Add(fields); root.Controls.Add(settings,0,1);
            var actions=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=6, Margin=new Padding(0,0,0,12) };
            foreach (int width in new[] {160,78,154,194,0,142}) actions.ColumnStyles.Add(width==0?new ColumnStyle(SizeType.Percent,100):new ColumnStyle(SizeType.Absolute,width));
            var buttons=new[] {start,stop,refresh,read,export}; int[] slots={0,1,2,3,5};
            for (int i=0;i<buttons.Length;i++) { buttons[i].Dock=DockStyle.Fill; buttons[i].Margin=new Padding(0,0,i==4?0:10,0); actions.Controls.Add(buttons[i],slots[i],0); }
            root.Controls.Add(actions,0,2);
            Columns(devices,"IP address","Name","Description","SNMP","Status"); devices.Columns[0].FillWeight=17; devices.Columns[1].FillWeight=23; devices.Columns[2].FillWeight=42; devices.Columns[3].FillWeight=9; devices.Columns[4].FillWeight=17;
            Columns(data,"OID","Field","Type","Value"); data.Columns[0].FillWeight=30; data.Columns[1].FillWeight=24; data.Columns[2].FillWeight=12; data.Columns[3].FillWeight=44;
            deviceTab.BackColor=dataTab.BackColor=Theme.Surface; deviceTab.Padding=dataTab.Padding=new Padding(12); deviceTab.Controls.Add(devices);
            var details=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=4 }; details.RowStyles.Add(new RowStyle(SizeType.Absolute,26)); details.RowStyles.Add(new RowStyle(SizeType.Absolute,42)); details.RowStyles.Add(new RowStyle(SizeType.Percent,100)); details.RowStyles.Add(new RowStyle(SizeType.Absolute,64)); details.Controls.Add(selected,0,0);
            var query=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=3, Margin=new Padding(0) }; query.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,174)); query.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); query.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,142));
            preset.Items.AddRange(new object[] {"System information","Printer MIB","Host devices","Standard MIB-II","Custom OID"}); preset.SelectedIndex=0; oid.Text="1.3.6.1.2.1.1"; readData.Dock=DockStyle.Fill; readData.Margin=new Padding(0,0,0,7); query.Controls.Add(preset,0,0); query.Controls.Add(oid,1,0); query.Controls.Add(readData,2,0); details.Controls.Add(query,0,1); details.Controls.Add(data,0,2); details.Controls.Add(value,0,3); dataTab.Controls.Add(details);
            tabs.TabPages.AddRange(new[] {deviceTab,dataTab}); root.Controls.Add(tabs,0,3); root.Controls.Add(progress,0,4); root.Controls.Add(status,0,5); Controls.Add(root);
            adapter.SelectedIndexChanged+=delegate { AdapterChanged(); };
            devices.SelectionChanged+=delegate { var item=CurrentDevice(); selected.Text=item==null?"Select a discovered device first.":item.IP+"  /  "+(String.IsNullOrEmpty(item.Name)?item.Description:item.Name); SetBusy(busy); };
            devices.SortCompare+=delegate(object sender,DataGridViewSortCompareEventArgs e) { if (e.Column.Index==0) { e.SortResult=IpRange.Number(IPAddress.Parse(Convert.ToString(e.CellValue1))).CompareTo(IpRange.Number(IPAddress.Parse(Convert.ToString(e.CellValue2)))); e.Handled=true; } };
            data.SelectionChanged+=delegate { value.Text=data.SelectedRows.Count==0?"":Convert.ToString(data.SelectedRows[0].Cells[3].Value); };
            preset.SelectedIndexChanged+=delegate { string[] roots={"1.3.6.1.2.1.1","1.3.6.1.2.1.43","1.3.6.1.2.1.25.3","1.3.6.1.2.1"}; if (preset.SelectedIndex<roots.Length) oid.Text=roots[preset.SelectedIndex]; };
            start.Click+=async delegate {await Scan();}; stop.Click+=delegate {if(cancellation!=null)cancellation.Cancel();}; refresh.Click+=delegate {LoadAdapters();};
            read.Click+=async delegate {tabs.SelectedTab=dataTab;await ReadData();}; readData.Click+=async delegate {await ReadData();}; export.Click+=delegate {ExportCsv();};
            tabs.SelectedIndexChanged+=delegate {SetBusy(busy);};
            if(!preview) Shown+=delegate {LoadAdapters();};
            FormClosing+=delegate(object sender,FormClosingEventArgs e) {if(busy){e.Cancel=true;closing=true;if(cancellation!=null)cancellation.Cancel();SetStatus("Stopping network requests...",false);}};
            SetBusy(false);
        }
        DeviceResult CurrentDevice() {return devices.SelectedRows.Count==0?null:devices.SelectedRows[0].Tag as DeviceResult;}
        void SetStatus(string text,bool success) {status.Text=text;status.ForeColor=success?Theme.Success:Theme.Muted;tips.SetToolTip(status,text);}
        void SetBusy(bool valueBusy) {
            busy=valueBusy;settings.Enabled=refresh.Enabled=start.Enabled=!busy;stop.Enabled=busy;
            read.Enabled=readData.Enabled=!busy&&CurrentDevice()!=null; preset.Enabled=oid.Enabled=!busy;
            export.Enabled=!busy&&(tabs.SelectedTab==dataTab?values.Count>0:results.Count>0);
        }
        void AdapterChanged() {
            var item=adapter.SelectedItem as Adapter; if(item==null)return;
            try {range.Text=item.DefaultRange();adapterInfo.Text=item.Description+"  |  Mask "+item.Mask+"  |  Interface "+item.Index;tips.SetToolTip(adapterInfo,adapterInfo.Text);}
            catch(Exception ex){SetStatus(ex.Message,false);}
            results.Clear();values.Clear();devices.Rows.Clear();data.Rows.Clear();deviceTab.Text="Devices (0)";SetBusy(false);
        }
        void LoadAdapters() {
            try {results.Clear();values.Clear();devices.Rows.Clear();data.Rows.Clear();deviceTab.Text="Devices (0)";adapter.Items.Clear();foreach(var item in Adapter.List())adapter.Items.Add(item);if(adapter.Items.Count>0)adapter.SelectedIndex=0;else SetStatus("No configured IPv4 adapters found. Configure an adapter, then refresh.",false);}
            catch(Exception ex){Error(ex);}
        }
        ScanOptions Options() {
            var item=adapter.SelectedItem as Adapter;
            if(item==null)throw new ArgumentException("Choose a configured IPv4 adapter.");
            if(!item.Up)throw new ArgumentException("This adapter is not connected. Connect or enable it, then refresh adapters.");
            var options=new ScanOptions {Adapter=item,Range=IpRange.Parse(range.Text,item),Community=community.Text,Version=version.SelectedIndex==0?-1:version.SelectedIndex==1?1:0,Port=(int)port.Value,Timeout=(int)timeout.Value,Retries=(int)retries.Value};options.Validate();return options;
        }
        void Error(Exception ex) {SetStatus("Operation could not finish: "+ex.Message,false);MessageBox.Show(this,ex.Message,"snmptoolRAV",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        void AddDevice(DeviceResult item) {if(IsDisposed)return;results.Add(item);int index=devices.Rows.Add(item.IP,item.Name,item.Description,item.VersionText,item.Status);devices.Rows[index].Tag=item;deviceTab.Text="Devices ("+results.Count+")";}
        async Task Scan() {
            ScanOptions options;try {options=Options();}catch(Exception ex){Error(ex);return;}
            results.Clear();values.Clear();devices.Rows.Clear();data.Rows.Clear();deviceTab.Text="Devices (0)";tabs.SelectedTab=deviceTab;progress.Value=0;
            cancellation=new CancellationTokenSource();SetBusy(true);SetStatus("Scanning "+options.Range+" through "+options.Adapter.Name+"...",false);
            var found=new Progress<DeviceResult>(item=>AddDevice(item));
            var report=new Progress<ScanProgress>(p=>{if(IsDisposed)return;progress.Value=Math.Min(100,(int)(100L*p.Completed/p.Total));SetStatus(p.Completed+" / "+p.Total+" IPs checked  |  "+p.Found+" SNMP devices  |  "+p.Errors+" network/response errors",false);});
            try {await Task.Run(()=>Scanner.Scan(options,item=>((IProgress<DeviceResult>)found).Report(item),p=>((IProgress<ScanProgress>)report).Report(p),cancellation.Token));SetStatus("Scan complete: "+results.Count+" SNMP devices found. No reply can mean wrong community/version, blocked UDP or SNMP disabled.",true);}
            catch(OperationCanceledException){SetStatus("Scan stopped. Discovered devices are kept.",false);}catch(Exception ex){Error(ex);}
            finally {cancellation.Dispose();cancellation=null;SetBusy(false);if(closing)Close();}
        }
        async Task ReadData() {
            var device=CurrentDevice();if(device==null)return;ScanOptions options;string rootOid;
            try {options=Options();rootOid=Ber.NormalizeOid(oid.Text);}catch(Exception ex){Error(ex);return;}
            values.Clear();data.Rows.Clear();value.Clear();dataDevice=device.IP;tabs.SelectedTab=dataTab;progress.Style=ProgressBarStyle.Marquee;cancellation=new CancellationTokenSource();SetBusy(true);SetStatus("Reading "+rootOid+" from "+device.IP+" (up to 512 values / 60 seconds)...",false);
            var rows=new Progress<DataRow>(row=>{if(IsDisposed)return;values.Add(row);data.Rows.Add(row.Oid,row.Name,row.Type,row.Value);});
            try {var result=await Task.Run(()=>Walker.Read(new SnmpClient(options.Adapter.Address,options.Adapter.Index,options.Port,options.Timeout),IPAddress.Parse(device.IP),options.Version<0?device.Version:options.Version,options.Community,rootOid,512,row=>((IProgress<DataRow>)rows).Report(row),cancellation.Token));SetStatus(result.Rows.Count+" values. "+result.Status,result.Status.StartsWith("Read complete",StringComparison.Ordinal));}
            catch(OperationCanceledException){SetStatus("Read stopped. Collected values are kept.",false);}catch(Exception ex){Error(ex);}
            finally {progress.Style=ProgressBarStyle.Continuous;progress.Value=0;cancellation.Dispose();cancellation=null;SetBusy(false);if(closing)Close();}
        }
        void ExportCsv() {
            bool detail=tabs.SelectedTab==dataTab;
            using(var dialog=new SaveFileDialog {Filter="CSV files (*.csv)|*.csv",FileName=detail?"snmp-data-"+dataDevice+".csv":"snmp-devices.csv",InitialDirectory=AppDomain.CurrentDomain.BaseDirectory}) {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try {var rows=new List<string[]>();if(detail){rows.Add(new[]{"IP address","OID","Field","Type","Value"});rows.AddRange(values.Select(v=>new[]{dataDevice,v.Oid,v.Name,v.Type,v.Value}));}else{rows.Add(new[]{"IP address","Name","Description","SNMP","Status"});rows.AddRange(results.Select(d=>new[]{d.IP,d.Name,d.Description,d.VersionText,d.Status}));}Csv.Write(dialog.FileName,rows);SetStatus("CSV saved: "+dialog.FileName,true);}catch(Exception ex){Error(ex);}
            }
        }
        internal static void Render(string directory) {
            Directory.CreateDirectory(directory);
            using(var form=new MainForm(true)) {
                form.adapter.Items.Add(new Adapter {Name="Ethernet",Description="Configured physical or virtual network adapter",Address=IPAddress.Parse("192.168.1.10"),Mask=IPAddress.Parse("255.255.255.0"),Index=7,Up=true});form.adapter.SelectedIndex=0;form.community.Text="public";
                form.AddDevice(new DeviceResult {IP="192.168.1.42",Name="Office printer",Description="Network printer / SNMP agent",Version=1,Status="Responding"});form.AddDevice(new DeviceResult {IP="192.168.1.80",Name="Storage",Description="Network storage appliance",Version=0,Status="Responding"});
                form.SetStatus("Scan complete: 2 SNMP devices found. Select a device to read its data.",true);form.SetBusy(false);form.Show();Application.DoEvents();SavePreview(form,directory,"discovery");
                form.tabs.SelectedTab=form.dataTab;form.data.Rows.Add("1.3.6.1.2.1.1.5.0","System name [0]","String","Office printer");form.data.Rows.Add("1.3.6.1.2.1.1.6.0","Location [0]","String","Home office");form.data.Rows.Add("1.3.6.1.2.1.43.11.1.1.6.1.1","Supply description [1.1]","String","Black toner");form.SetStatus("Sample device data - values depend on what each device exposes.",false);Application.DoEvents();SavePreview(form,directory,"device-data");
                form.ClientSize=new Size(944,661);Application.DoEvents();SavePreview(form,directory,"compact");form.Hide();
            }
        }
        static void SavePreview(MainForm form,string directory,string name){using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(directory,name+".png"),System.Drawing.Imaging.ImageFormat.Png);}}
        protected override void Dispose(bool disposing){if(disposing)tips.Dispose();base.Dispose(disposing);}
    }
}
