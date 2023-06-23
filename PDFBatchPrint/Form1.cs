using System;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using PdfiumViewer;

namespace PDFBatchPrint
{
    public partial class Form1 : Form
    {

        [DllImport("kernel32.dll")]
        static extern IntPtr GlobalLock(IntPtr hMem);
        [DllImport("kernel32.dll")]
        static extern bool GlobalUnlock(IntPtr hMem);
        [DllImport("kernel32.dll")]
        static extern bool GlobalFree(IntPtr hMem);
        [DllImport("winspool.Drv", EntryPoint = "DocumentPropertiesW", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        static extern int DocumentProperties(IntPtr hwnd, IntPtr hPrinter, [MarshalAs(UnmanagedType.LPWStr)] string pDeviceName, IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);

        private const int DM_PROMPT = 4;
        private const int DM_OUT_BUFFER = 2;
        private const int DM_IN_BUFFER = 8;
        PrinterSettings printerSettings = null;
        private bool stopping = false;
        private int printingIndex = -1;

        public Form1()
        {
            InitializeComponent();
            this.dataGrid.AllowDrop = true;
            this.dataGrid.DragEnter += new DragEventHandler(Form1_DragEnter);
            this.dataGrid.DragDrop += new DragEventHandler(Form1_DragDrop);

            foreach (var printer in PrinterSettings.InstalledPrinters)
                this.comboPrinterNames.Items.Add(printer);

            printerSettings = new PrinterSettings();
            this.comboPrinterNames.SelectedItem = printerSettings.PrinterName;
        }

        void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        void Form1_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (string file in files)
            {
                if (file.ToLower().EndsWith(".pdf"))
                {
                    dataGrid.Rows.Add(file, true, "");
                }
            }
        }

        private void buttonPrintClick(object sender, EventArgs e)
        {
            int count = 0;
            for (int i = 0; i < dataGrid.Rows.Count; i++)
            {
                var row = dataGrid.Rows[i];
                if ((bool)row.Cells[1].Value == true) count++;
            }
            if (count == 0)
            {
                MessageBox.Show(this, "There are no files selected to print.\nPlease drag n' drop some files into the list box above.");
                return;
            }
            else
            {
                printerSettings.PrinterName = (string)this.comboPrinterNames.SelectedItem;
                IntPtr hDevMode = printerSettings.GetHdevmode(printerSettings.DefaultPageSettings);
                IntPtr pDevMode = GlobalLock(hDevMode);
                int sizeNeeded = DocumentProperties(this.Handle, IntPtr.Zero, printerSettings.PrinterName, IntPtr.Zero, pDevMode, 0);
                IntPtr devModeData = Marshal.AllocHGlobal(sizeNeeded);
                long userChoice = DocumentProperties(this.Handle, IntPtr.Zero, printerSettings.PrinterName, devModeData, pDevMode, DM_IN_BUFFER | DM_PROMPT | DM_OUT_BUFFER);
                long IDOK = (long)DialogResult.OK;
                if (userChoice == IDOK)
                {
                    printerSettings.SetHdevmode(devModeData);
                    printerSettings.DefaultPageSettings.SetHdevmode(devModeData);
                    this.printingIndex = 0;
                    this.stopping = false;
                    buttonPrint.Enabled = false;
                    buttonPrint.Text = "Printing...";
                    buttonStop.Enabled = true;
                    buttonStop.Text = "Stop";
                    buttonDeselected.Enabled = false;
                    buttonClear.Enabled = false;
                    print();
                }
                GlobalUnlock(hDevMode);
                GlobalFree(hDevMode);
                Marshal.FreeHGlobal(devModeData);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonClearClick(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Are you sure you want to clear the list of items to be printed?", "Confirm Clear", MessageBoxButtons.YesNo) == DialogResult.Yes)
                this.dataGrid.Rows.Clear();
        }
        private void print()
        {
            Form1 self = this;
            if (!this.stopping)
            {
                for (; this.printingIndex < dataGrid.Rows.Count; this.printingIndex++)
                {
                    var row = dataGrid.Rows[printingIndex];
                    if (!(bool)row.Cells[1].Value) continue;
                    string filename = row.Cells[0].Value as string;
                    row.Cells[2].Value = "Printing...";
                    PrintThread pt = new PrintThread(filename, printerSettings); ;
                    ThreadStart starter = new ThreadStart(pt.Print);
                    starter += () =>
                    {
                        self.Invoke(new Action(() =>
                        {
                            row.Cells[1].Value = false;
                            if (pt.Result)
                            {
                                row.Cells[2].Value = "Printed";
                            } else
                            {
                                row.Cells[2].Value = "Failed";
                            }
                            print();

                        }));
                    };
                    Thread t = new Thread(starter);
                    t.Start();
                    return;
                }
            }
            // reset UI
            this.stopping = false;
            buttonPrint.Enabled = true;
            buttonPrint.Text = "Print...";
            buttonStop.Enabled = false;
            buttonStop.Text = "Stop";
            buttonDeselected.Enabled = true;
            buttonClear.Enabled = true;
        }

        private void buttonDeselected_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Are you sure you want to remove deselected items?", "Confirm Clear", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                for (int i = dataGrid.Rows.Count - 1; i >= 0; i--)
                {
                    var row = dataGrid.Rows[i];
                    if (!(bool)row.Cells[1].Value)
                    {
                        dataGrid.Rows.RemoveAt(i);
                    }
                }
            }
        }

        private void buttonStop_Click(object sender, EventArgs e)
        {
            buttonStop.Text = "Stopping";
            buttonStop.Enabled = false;
            this.stopping = true;
        }
    }

    class PrintThread
    {
        private string filename;
        private PrinterSettings settings;
        public bool Result { get; set; }
        private EventWaitHandle wh = new AutoResetEvent(false);

        public PrintThread(string filename, PrinterSettings settings)
        {
            this.filename = filename;
            this.settings = settings;
        }

        public void Print()
        {
            try
            {

                // Now print the PDF document
                using (var document = PdfDocument.Load(filename))
                {
                    using (var printDocument = document.CreatePrintDocument())
                    {
                        printDocument.PrinterSettings = settings;
                        printDocument.PrintController = new StandardPrintController();
                        printDocument.EndPrint += finish;
                        printDocument.Print();
                        wh.WaitOne();
                    }
                }
                Result = true;
            }
            catch
            {
                Result = false;
            }
        }

        private void finish(object sender, PrintEventArgs e)
        {
            wh.Set();
        }
    }
}
