namespace ForceDeleter;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.SuspendLayout();

        // Form settings
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
        this.ClientSize = new System.Drawing.Size(760, 680);
        this.MinimumSize = new System.Drawing.Size(700, 600);
        this.Text = "Universal Force Deleter (NanaRun + ForceDelete + DeadLock)";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        this.AllowDrop = true;
        this.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        this.BackColor = System.Drawing.Color.FromArgb(248, 249, 250);

        this.ResumeLayout(false);
    }
}
