using PITrackerCore;

namespace PiTracker
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void startC_Click(object sender, EventArgs e)
        {
            // Create the interface and trigger the algorithm
            var p = new PITrackerCore.Program();
            if (seqSourceC.Checked)
            {
                p.CameraSource = new ImageSequenceSource(imageSourceC.Text);
            }
        }
    }
}
