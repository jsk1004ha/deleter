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

        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
        this.ClientSize = new System.Drawing.Size(820, 600);
        this.MinimumSize = new System.Drawing.Size(740, 520);
        this.Text = "Delete";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        this.AllowDrop = true;

        this.ResumeLayout(false);
    }
}
