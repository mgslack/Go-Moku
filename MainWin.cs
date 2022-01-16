using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Media;
using System.Collections.Generic;
using Microsoft.Win32;
using GameStatistics;
using CustomMessageBox;

/*
 * Game of Go-Moku, using old school 19x19 Go board rather than modern 15x15 board.  Based
 * move evaluation and hints on Borland's Turbo Gameworks toolbox version.
 * 
 * A game is started when the program loads.  Player always starts and is the
 * white (cross) stones.  Has auto play feature so can have computer play both
 * sides.
 * 
 * Author: Michael Slack
 * Date Written: 2021-11-27
 * 
 * ----------------------------------------------------------------------------
 * 
 * Revised: 2021-12-02 - Changed computer winning message to reflect may not be
 *                       playing just 'nought' stones anymore.  Change made to
 *                       allow for 'switching' sides and did not catch the win
 *                       message till after publishing.
 *          2022-01-16 - Fixed a minor nit with the display of the moves made
 *                       list.
 * 
 */
namespace Go_Moku
{
    #region Enums
    public enum TypeOfWin { Null = -1, Horiz = 0, DownLeft = 1, DownRight = 2, Vert = 3 };

    public enum BoardType { Empty = -1, Nought = 0, Cross = 1 };
    #endregion

    public partial class MainWin : Form
    {
        #region Private consts/statics
        private const string HTML_HELP_FILE = "Go-Moku_help.html";
        private const int MAX_GRID = 19;
        private const int NUM_PLAYERS = 2;
        private const int WIN_LINE_LEN = 5;
        private const int MAX_DIRECTION = 4;
        private const int ATTACK_FACTOR = 4;  // importance of attack, 1 - 16
        private static readonly int[] WEIGHT = { 0, 0, 4, 20, 100, 500, 0 };
        #endregion

        #region Registry consts
        private const string REG_NAME = @"HKEY_CURRENT_USER\Software\Slack and Associates\Games\GoMoku";
        private const string REG_KEY1 = "PosX";
        private const string REG_KEY2 = "PosY";
        private const string REG_CS_AUTO = "Number of Autoplay Wins";
        #endregion

        #region Private vars
        private BoardType[,] board = new BoardType[MAX_GRID, MAX_GRID];
        private BoardType player;  // will only be Cross or Nought
        // number of pieces in each of all possible lines for each player
        private int[,,,] line = new int[MAX_DIRECTION, MAX_GRID, MAX_GRID, NUM_PLAYERS];
        // value of each position for each player
        private int[,,] value = new int[MAX_GRID, MAX_GRID, NUM_PLAYERS];
        private int totalLines = 0, curX = (MAX_GRID + 1) / 2, curY = (MAX_GRID + 1) / 2;
        private int oldX = -1, oldY = -1, winX = 0, winY = 0;
        private bool gameWon = false, autoPlay = false, playing = false,
            drawCursor = true, toggledAutoOrSwitch = false;
        private Queue<string> movesMade = new Queue<string>();
        private Statistics stats = new Statistics(REG_NAME);
        #endregion

        #region Event handlers
        private event EventHandler PlayerMove;
        private event EventHandler ComputerMove;
        #endregion

        // --------------------------------------------------------------------

        #region Private methods
        private void LoadRegistryValues()
        {
            int winX = -1, winY = -1;

            try
            {
                winX = (int)Registry.GetValue(REG_NAME, REG_KEY1, winX);
                winY = (int)Registry.GetValue(REG_NAME, REG_KEY2, winY);
            }
            catch (Exception) { /* ignore, go with defaults, but could use MessageBox.Show(e.Message); */ }

            if ((winX != -1) && (winY != -1)) this.SetDesktopLocation(winX, winY);
        }

        private void SetupContextMenu()
        {
            ContextMenu mnu = new ContextMenu();
            MenuItem mnuStats = new MenuItem("Game Statistics");
            MenuItem mnuMoves = new MenuItem("Review Moves Made");
            MenuItem sep = new MenuItem("-");
            MenuItem mnuHelp = new MenuItem("Help");
            MenuItem mnuAbout = new MenuItem("About");

            mnuStats.Click += new EventHandler(MnuStats_Click);
            mnuMoves.Click += new EventHandler(MnuMoves_Click);
            mnuHelp.Click += new EventHandler(MnuHelp_Click);
            mnuAbout.Click += new EventHandler(MnuAbout_Click);
            mnu.MenuItems.AddRange(new MenuItem[] { mnuStats, mnuMoves, sep, mnuHelp, mnuAbout });
            this.ContextMenu = mnu;
        }

        private void SetupStructures()
        {
            PlayerMove += CustomPlayerMove;
            ComputerMove += CustomComputerMove;
        }

        private void DoEvent(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void DisplayCell(int c, int r)
        {
            TableLayoutCellPaintEventArgs arg;
            Rectangle rect;
            int x = 0, y = 0; // assuming no cell borders

            // get column start in pixels
            if (c > 0)
                for (int i = 0; i < c; i++) x += (int)tlpGoTable.GetColumnWidths()[i];
            // get row start in pixels
            if (r > 0)
                for (int i = 0; i < r; i++) y += (int)tlpGoTable.GetRowHeights()[i];
            // rect width/height assumes no single line cell borders
            rect = new Rectangle(x, y, (int)tlpGoTable.GetColumnWidths()[c],
                (int)tlpGoTable.GetRowHeights()[r]);
            arg = new TableLayoutCellPaintEventArgs(tlpGoTable.CreateGraphics(), rect, rect, c, r);
            TlpGoTable_CellPaint(this, arg);
        }

        // Method adapted from: stackoverflow.com/a/15449969
        // - found at: https://stackoverflow.com/questions/15449504/how-do-i-determine-the-cell-being-clicked-on-in-a-tablelayoutpanel
        // - answered Jan 30 '17 at 12:03 by Peter Gordon
        // retuns the col/row index of a cell clicked on in a TableLayoutPanel
        private Point? GetCellIndex(TableLayoutPanel tlp, Point point)
        {
            if (point.X > tlp.Width || point.Y > tlp.Height)
                return null;

            int w = 0, h = 0;
            int[] widths = tlp.GetColumnWidths(), heights = tlp.GetRowHeights();

            int i;
            for (i = 0; i < widths.Length && point.X > w; i++)
                w += widths[i];
            int col = i - 1;

            for (i = 0; i < heights.Length && point.Y + tlp.VerticalScroll.Value > h; i++)
                h += heights[i];
            int row = i - 1;

            return new Point(col, row);
        }

        private void DisplayNewCurPos(int nx, int ny)
        {
            oldX = curX; oldY = curY;
            curX = nx; curY = ny;
            DisplayCell(oldX, oldY);
            DisplayCell(curX, curY);
            Application.DoEvents();
        }

        private void MoveCur(int dx, int dy)
        {
            if ((curX + dx) > 0 && (curX + dx) <= MAX_GRID &&
                (curY + dy) > 0 && (curY + dy) <= MAX_GRID)
                DisplayNewCurPos(curX + dx, curY + dy);
        }

        private void ResetGame()
        {
            // clear board, structures used
            for (int i = 0; i < MAX_GRID; i++)
                for (int j = 0; j < MAX_GRID; j++)
                {
                    board[i, j] = BoardType.Empty;
                    for (int p = 0; p < NUM_PLAYERS; p++)
                    {
                        value[i, j, p] = 0;
                        for (int d = 0; d < MAX_DIRECTION; d++)
                            line[d, i, j, p] = 0;
                    }
                }
            player = BoardType.Cross;
            totalLines = 2 * 2 * (MAX_GRID * (MAX_GRID - 4) + (MAX_GRID - 4) * (MAX_GRID - 4));
            gameWon = false; autoPlay = false;
            curX = (MAX_GRID + 1) / 2; curY = curX; oldX = -1; oldY = -1; winX = 0; winY = 0;
            lblMove.Text = "";
            movesMade.Clear();
            tlpGoTable.Refresh();
        }

        private BoardType OpponentColor()
        {
            return (player == BoardType.Nought) ? BoardType.Cross : BoardType.Nought;
        }

        private void SetPlayer()
        {
            if (!toggledAutoOrSwitch) player = OpponentColor();
            toggledAutoOrSwitch = false;
        }

        private Tuple<int, int> FindMove()
        {
            BoardType opponent = OpponentColor();
            int max = -1, valu;
            // start with board middle to select if no higher value grid locations
            int mx = ((MAX_GRID + 1) / 2) - 1, my = ((MAX_GRID + 1) / 2) - 1;

            if (board[mx, my] == BoardType.Empty) max = ATTACK_FACTOR;
            // evaluation is value of sq for player (attack pts) plus value of
            // sq for opponent (def pts).  Attack is more important since better
            // to get 5 in a row versus block opponent from getting it
            for (int c = 0; c < MAX_GRID; c++)
                for (int r = 0; r < MAX_GRID; r++)
                {
                    if (board[c, r] == BoardType.Empty)
                    {
                        valu = value[c, r, (int)player] * (16 + ATTACK_FACTOR) / 16 +
                            value[c, r, (int)opponent] +
                            SingleRandom.Instance.Next(ATTACK_FACTOR + 1);
                        if (valu > max)
                        {
                            mx = c; my = r; max = valu;
                        }
                    }
                }

            return Tuple.Create(mx + 1, my + 1);
        }

        private void DisplayMoveMade()
        {
            char[] chars = " ABCDEFGHIJKLMNOPQRS".ToCharArray();
            int bPos = MAX_GRID - curY + 1;

            lblMove.Text = player.ToString() + " : " + chars[curX] + bPos;
            movesMade.Enqueue(lblMove.Text);
        }

        private TypeOfWin EvaluateLine(TypeOfWin wl, TypeOfWin dir, int x, int y)
        {
            TypeOfWin ret = wl;

            line[(int)dir, x, y, (int)player]++;
            if (line[(int)dir, x, y, (int)player] == 1) totalLines--;
            if (line[(int)dir, x, y, (int)player] == 5) gameWon = true;
            if (gameWon && wl == TypeOfWin.Null) ret = dir;

            return ret;
        }

        private void UpdateValues(TypeOfWin dir, int x, int y, int vx, int vy)
        {
            BoardType opponent = OpponentColor();

            if (line[(int)dir, x, y, (int)opponent] == 0)
            {
                value[vx, vy, (int)player] += WEIGHT[line[(int)dir, x, y, (int)player] + 1] -
                    WEIGHT[line[(int)dir, x, y, (int)player]];
            }
            else
            {
                if (line[(int)dir, x, y, (int)player] == 1)
                {
                    value[vx, vy, (int)opponent] -= WEIGHT[line[(int)dir, x, y, (int)opponent] + 1];
                }
            }
        }

        private void SetWinningLine(TypeOfWin wl)
        {
            int dx = 0, dy = 0, x = curX - 1, y = curY - 1;

            switch (wl)
            {
                case TypeOfWin.Horiz: dx = 1; dy = 0; break;
                case TypeOfWin.DownLeft: dx = 1; dy = 1; break;
                case TypeOfWin.DownRight: dx = -1; dy = 1; break;
                case TypeOfWin.Vert: dx = 0; dy = 1; break;
                default: break;
            }

            while ((x + dx) >= 0 && (x + dx) < MAX_GRID &&
                (y + dy) >= 0 && (y + dy) < MAX_GRID &&
                board[x + dx, y + dy] != BoardType.Empty &&
                board[x + dx, y + dy] == player)
            {
                x += dx; y += dy;
            }

            for (int i = 0; i < WIN_LINE_LEN; i++)
            {
                winX = x + 1; winY = y + 1;
                DisplayCell(winX, winY);
                x -= dx; y -= dy;
            }
            Application.DoEvents();
        }

        private void MakeMove(int x, int y)
        {
            TypeOfWin winningLine = TypeOfWin.Null;
            int x1, y1;

            // horizontal lines
            for (int k = 0; k <= 4; k++)
            {
                x1 = x - k; y1 = y;
                if (0 <= x1 && x1 <= (MAX_GRID - 5))
                {
                    winningLine = EvaluateLine(winningLine, TypeOfWin.Horiz, x1, y1);
                    for (int l = 0; l <= 4; l++)
                        UpdateValues(TypeOfWin.Horiz, x1, y1, x1 + l, y1);
                }
            }
            // diagonal, ll to ur
            for (int k = 0; k <= 4; k++)
            {
                x1 = x - k; y1 = y - k;
                if (0 <= x1 && x1 <= (MAX_GRID - 5) && 0 <= y1 && y1 <= (MAX_GRID - 5))
                {
                    winningLine = EvaluateLine(winningLine, TypeOfWin.DownLeft, x1, y1);
                    for (int l = 0; l <= 4; l++)
                        UpdateValues(TypeOfWin.DownLeft, x1, y1, x1 + l, y1 + l);
                }
            }
            // diagonal, dr to ul
            for (int k = 0; k <= 4; k++)
            {
                x1 = x + k; y1 = y - k;
                if (4 <= x1 && x1 <= (MAX_GRID - 1) && 0 <= y1 && y1 <= (MAX_GRID - 5))
                {
                    winningLine = EvaluateLine(winningLine, TypeOfWin.DownRight, x1, y1);
                    for (int l = 0; l <= 4; l++)
                        UpdateValues(TypeOfWin.DownRight, x1, y1, x1 - l, y1 + l);
                }
            }
            // vertical lines
            for (int k = 0; k <= 4; k++)
            {
                x1 = x; y1 = y - k;
                if (0 <= y1 && y1 <= (MAX_GRID - 5))
                {
                    winningLine = EvaluateLine(winningLine, TypeOfWin.Vert, x1, y1);
                    for (int l = 0; l <= 4; l++)
                        UpdateValues(TypeOfWin.Vert, x1, y1, x1, y1 + l);
                }
            }

            board[x, y] = player;
            DisplayMoveMade();
            drawCursor = false;
            DisplayCell(x + 1, y + 1);
            drawCursor = true;
            if (gameWon) SetWinningLine(winningLine);
        }

        private bool GameOver()
        {
            return gameWon || totalLines <= 0;
        }

        private void CustomXXXMove()
        {
            Tuple<int, int> move = FindMove();
            DisplayNewCurPos(move.Item1, move.Item2);
            MakeMove(curX - 1, curY - 1);
            if (gameWon) movesMade.Enqueue(player.ToString() + " won!");
        }
        #endregion

        // --------------------------------------------------------------------

        public MainWin()
        {
            InitializeComponent();
        }

        // --------------------------------------------------------------------

        #region Form/Control events
        private void MainWin_Load(object sender, EventArgs e)
        {
            LoadRegistryValues();
            SetupContextMenu();
            SetupStructures();
            ResetGame();
            stats.GameName = this.Text;
            stats.StartGame(false);
        }

        private void MainWin_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (this.WindowState == FormWindowState.Normal)
            {
                Registry.SetValue(REG_NAME, REG_KEY1, this.Location.X);
                Registry.SetValue(REG_NAME, REG_KEY2, this.Location.Y);
            }
        }

        private void BtnStart_Click(object sender, EventArgs e)
        {
            if (!playing && !autoPlay)
            {
                bool go = true;
                if (!gameWon)
                {
                    DialogResult res = MsgBox.Show(this,
                        "Game is in progress, terminate and start new game?", this.Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxIcon.Question,
                        null, MessageBoxDefaultButton.Button2, Point.Empty);
                    if (res == DialogResult.Yes)
                        stats.GameDone();
                    else
                        go = false;
                }
                if (go)
                {
                    ResetGame();
                    stats.StartGame(true);
                    btnPlay.Focus();
                }
            }
        }

        private void BtnQuit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void BtnPlay_Click(object sender, EventArgs e)
        {
            if (!playing && !autoPlay && !gameWon)
            {
                if (board[curX - 1, curY - 1] == BoardType.Empty)
                {
                    MakeMove(curX - 1, curY - 1);
                    stats.MoveMade();
                    if (gameWon)
                    {
                        MsgBox.Show(this, "Congratulations, you've won.", this.Text, MessageBoxButtons.OK,
                            MessageBoxIcon.Information, MessageBoxIcon.Information);
                        stats.GameWon(0);
                        movesMade.Enqueue(player.ToString() + " won!");
                    }
                    DoEvent(ComputerMove);
                }
                else
                {
                    SystemSounds.Beep.Play();
                }
            }
        }

        private void BtnHint_Click(object sender, EventArgs e)
        {
            if (!playing && !autoPlay && !gameWon)
            {
                Tuple<int, int> hint = FindMove();
                DisplayNewCurPos(hint.Item1, hint.Item2);
                btnPlay.Focus();
            }
        }

        private void BtnSwitch_Click(object sender, EventArgs e)
        {
            if (!playing && !autoPlay && !gameWon)
            {
                movesMade.Enqueue("Switched Sides");
                toggledAutoOrSwitch = true;
                btnPlay.Focus();
                DoEvent(ComputerMove);
            }
        }

        private void BtnAuto_Click(object sender, EventArgs e)
        {
            if (!playing && !gameWon)
            {
                autoPlay = !autoPlay;
                if (autoPlay)
                {
                    toggledAutoOrSwitch = true;
                    DoEvent(PlayerMove);
                }
            }
        }

        private void MainWin_KeyUp(object sender, KeyEventArgs e)
        {
            if (!playing && !autoPlay)
            {
                int K = e.KeyValue;

                // change to set key value if using numpad w/o numlock
                switch (e.KeyCode)
                {
                    case Keys.Home: K = 103; break;
                    case Keys.Up: K = 104; break;
                    case Keys.PageUp: K = 105; break;
                    case Keys.Left: K = 100; break;
                    case Keys.Right: K = 102; break;
                    case Keys.End: K = 97; break;
                    case Keys.Down: K = 98; break;
                    case Keys.PageDown: K = 99; break;
                    default: break;
                }

                if (K >= 97 && K <= 105)  // keypad 1 - 9
                {
                    K -= 96; // 1 - 9
                    e.Handled = true;
                    switch (K)
                    {
                        case 1: MoveCur(-1, 1); break;
                        case 2: MoveCur(0, 1); break;
                        case 3: MoveCur(1, 1); break;
                        case 4: MoveCur(-1, 0); break;
                        case 6: MoveCur(1, 0); break;
                        case 7: MoveCur(-1, -1); break;
                        case 8: MoveCur(0, -1); break;
                        case 9: MoveCur(1, -1); break;
                        default: e.Handled = false; break;
                    }
                    btnPlay.Focus();
                }
            }
        }

        private void TlpGoTable_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !playing && !autoPlay)
            {
                Point cell = (Point)GetCellIndex((TableLayoutPanel)sender, e.Location);
                if (cell != null)
                    if (cell.X > 0 && cell.X <= MAX_GRID && cell.Y > 0 && cell.Y <= MAX_GRID)
                        DisplayNewCurPos(cell.X, cell.Y);
            }
        }

        private void TlpGoTable_CellPaint(object sender, TableLayoutCellPaintEventArgs e)
        {
            if (e.Column > 0 && e.Row > 0)
            { // only paint columns and rows 1 - 19
                Graphics g = e.Graphics;
                int c = e.Column - 1, r = e.Row - 1;

                // draw stone, if placed
                if (board[c, r] != BoardType.Empty)
                {
                    g.DrawImage(ilStones.Images[(int)board[c, r]], e.CellBounds.X + 1, e.CellBounds.Y + 1);
                }
                // draw cursor highlight (current cursor position, erase from old)
                if (drawCursor)
                {
                    if ((e.Column == curX && e.Row == curY) || (e.Column == oldX && e.Row == oldY))
                    {
                        // if using white board, use Color.Red, brown board, Color.LightPink
                        GDI.DrawXORRectangle(g, new Pen(Color.LightPink), e.CellBounds);
                    }
                }
                // draw highlight around winning line...
                if (gameWon)
                {
                    // for brown board, new Pen(Color.DarkGreen), white board, use Color.Green
                    if (winX == e.Column && winY == e.Row)
                        GDI.DrawXORRectangle(g, new Pen(Color.DarkGreen), e.CellBounds);
                }
            }
        }

        private void MnuStats_Click(object sender, EventArgs e)
        {
            stats.ShowStatistics(this);
        }

        private void MnuMoves_Click(object sender, EventArgs e)
        {
            MovesDlg dlg = new MovesDlg
            {
                Moves = movesMade,
                GameWon = gameWon
            };

            _ = dlg.ShowDialog();
            dlg.Dispose();
        }

        private void MnuHelp_Click(object sender, EventArgs e)
        {
            var asm = Assembly.GetEntryAssembly();
            var asmLocation = Path.GetDirectoryName(asm.Location);
            var htmlPath = Path.Combine(asmLocation, HTML_HELP_FILE);

            try
            {
                Process.Start(htmlPath);
            }
            catch (Exception ex)
            {
                MsgBox.Show(this, "Cannot load help: " + ex.Message, this.Text, MessageBoxButtons.OK,
                    MessageBoxIcon.Information, MessageBoxIcon.Information);
            }
        }

        private void MnuAbout_Click(object sender, EventArgs e)
        {
            AboutBox about = new AboutBox();

            about.ShowDialog(this);
            about.Dispose();
        }
        #endregion

        // --------------------------------------------------------------------

        #region Move events
        private void CustomPlayerMove(object sender, EventArgs e)
        {
            SetPlayer();
            if (autoPlay && !gameWon)
            {
                CustomXXXMove();
                if (gameWon)
                {
                    MsgBox.Show(this, "Auto-played player won.", this.Text, MessageBoxButtons.OK,
                        MessageBoxIcon.Information, MessageBoxIcon.Information);
                    stats.IncCustomStatistic(REG_CS_AUTO);
                    stats.GameWon(0);
                    autoPlay = false;
                }
                else
                {
                    DoEvent(ComputerMove);
                }
            }
        }

        private void CustomComputerMove(object sender, EventArgs e)
        {
            SetPlayer();
            if (GameOver())
            {
                if (!gameWon)
                {
                    MsgBox.Show(this, "Tie game, no winning moves left.", this.Text, MessageBoxButtons.OK,
                        MessageBoxIcon.Information, MessageBoxIcon.Information);
                    movesMade.Enqueue("Tie game!");
                    gameWon = true;
                    stats.GameTied();
                    autoPlay = false;
                }
            }
            else
            {
                playing = true;
                CustomXXXMove();
                playing = false;
                if (gameWon)
                {
                    MsgBox.Show(this, player.ToString() + " stones wins (you lose).", this.Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxIcon.Information);
                    stats.GameLost(0);
                    autoPlay = false;
                }
                else
                {
                    DoEvent(PlayerMove);
                }
            }
        }
        #endregion
    }
}