using System;
using System.Drawing;
using System.Windows.Forms;

namespace Installer
{
    // 右栏只有一屏：方案树。原来那套「一个大类一个列表视图 + 顶部页签切换」已经整个撤掉，
    // 这里只留树自己的宿主、标题和勾选同步。
    public partial class MainForm
    {
        /// <summary>方案树：按「能替换的东西」看这套方案，右边是候选 MOD。</summary>
        internal void ShowTree()
        {
            EnsureTree();
            if (lblLoading != null)              // 读完第一屏就撤掉读取提示
            {
                viewHost.Controls.Remove(lblLoading);
                lblLoading.Dispose();
                lblLoading = null;
            }
            viewHost.Visible = true;
            treeView.Visible = true;
            treeView.BringToFront();
            treeView.Rebuild();
            UpdateModCaption();
        }

        private void EnsureTree()
        {
            if (treeView != null) return;
            treeView = new CatTree(this);
            treeView.SetBounds(0, 0, Math.Max(200, viewHost.ClientSize.Width), Math.Max(120, viewHost.ClientSize.Height));
            viewHost.Controls.Add(treeView);
        }

        /// <summary>勾选变了：树的勾、套装计数、底部已选数一起对齐（不重建，滚动位置不动）。</summary>
        internal void SyncPicks()
        {
            if (treeView != null && treeView.Visible) treeView.Sync();
            UpdateSel();
            UpdateSchemeLabel();
        }

        /// <summary>右栏标题栏：几个位置、已选几个。</summary>
        internal void UpdateModCaption()
        {
            if (gMod == null) return;
            int sc = treeView == null ? 0 : treeView.SlotCount;
            int sk = treeView == null ? 0 : treeView.SlotCheckedCount;
            gMod.Caption = "方案树 · " + sc + " 个位置 · 已选 " + sk
                         + (treeView != null && treeView.Unmatched > 0 ? " · " + treeView.Unmatched + " 个未归类" : "");
        }
    }
}
