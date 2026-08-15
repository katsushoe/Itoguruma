#nullable enable

namespace Itoguruma.Viewer;

partial class MessageDetailsForm
{
    private System.ComponentModel.IContainer? components = null;
    private DataGridView detailsGrid = null!;
    private DataGridViewTextBoxColumn itemColumn = null!;
    private DataGridViewTextBoxColumn valueColumn = null!;
    private Button closeButton = null!;
    private TableLayoutPanel rootLayout = null!;
    private FlowLayoutPanel buttonLayout = null!;

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
        detailsGrid = new DataGridView();
        itemColumn = new DataGridViewTextBoxColumn();
        valueColumn = new DataGridViewTextBoxColumn();
        closeButton = new Button();
        rootLayout = new TableLayoutPanel();
        buttonLayout = new FlowLayoutPanel();
        ((System.ComponentModel.ISupportInitialize)detailsGrid).BeginInit();
        rootLayout.SuspendLayout();
        buttonLayout.SuspendLayout();
        SuspendLayout();
        //
        // detailsGrid
        //
        detailsGrid.AllowUserToAddRows = false;
        detailsGrid.AllowUserToDeleteRows = false;
        detailsGrid.AllowUserToResizeRows = false;
        detailsGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        detailsGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        detailsGrid.Columns.AddRange(new DataGridViewColumn[] { itemColumn, valueColumn });
        detailsGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        detailsGrid.Dock = DockStyle.Fill;
        detailsGrid.Location = new Point(12, 12);
        detailsGrid.MultiSelect = false;
        detailsGrid.Name = "detailsGrid";
        detailsGrid.ReadOnly = true;
        detailsGrid.RowHeadersVisible = false;
        detailsGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        detailsGrid.Size = new Size(760, 493);
        detailsGrid.TabIndex = 0;
        //
        // itemColumn
        //
        itemColumn.HeaderText = "項目";
        itemColumn.Name = "itemColumn";
        itemColumn.ReadOnly = true;
        itemColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
        itemColumn.Width = 150;
        //
        // valueColumn
        //
        valueColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        valueColumn.HeaderText = "値";
        valueColumn.Name = "valueColumn";
        valueColumn.ReadOnly = true;
        valueColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
        //
        // closeButton
        //
        closeButton.AutoSize = true;
        closeButton.DialogResult = DialogResult.Cancel;
        closeButton.Location = new Point(685, 3);
        closeButton.Name = "closeButton";
        closeButton.Size = new Size(75, 25);
        closeButton.TabIndex = 0;
        closeButton.Text = "閉じる";
        closeButton.UseVisualStyleBackColor = true;
        //
        // rootLayout
        //
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(detailsGrid, 0, 0);
        rootLayout.Controls.Add(buttonLayout, 0, 1);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Location = new Point(0, 0);
        rootLayout.Name = "rootLayout";
        rootLayout.Padding = new Padding(12);
        rootLayout.RowCount = 2;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.Size = new Size(784, 561);
        rootLayout.TabIndex = 0;
        //
        // buttonLayout
        //
        buttonLayout.AutoSize = true;
        buttonLayout.Controls.Add(closeButton);
        buttonLayout.Dock = DockStyle.Fill;
        buttonLayout.FlowDirection = FlowDirection.RightToLeft;
        buttonLayout.Location = new Point(12, 508);
        buttonLayout.Margin = new Padding(0, 3, 0, 0);
        buttonLayout.Name = "buttonLayout";
        buttonLayout.Size = new Size(760, 41);
        buttonLayout.TabIndex = 1;
        //
        // MessageDetailsForm
        //
        AcceptButton = closeButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = closeButton;
        ClientSize = new Size(784, 561);
        Controls.Add(rootLayout);
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(640, 480);
        Name = "MessageDetailsForm";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "メッセージ詳細";
        ((System.ComponentModel.ISupportInitialize)detailsGrid).EndInit();
        rootLayout.ResumeLayout(false);
        rootLayout.PerformLayout();
        buttonLayout.ResumeLayout(false);
        buttonLayout.PerformLayout();
        ResumeLayout(false);
    }
}
