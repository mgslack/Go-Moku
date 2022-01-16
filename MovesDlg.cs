using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

/*
 * Partial class of the game moves list dialog for the Go-Moku game.  Will take
 * the current move queue and display the list so player can see moves made
 * durring and after a game.
 * 
 * Author: Michael Slack
 * Date Written: 2021-11-30
 * 
 * ----------------------------------------------------------------------------
 * 
 * Revised: 2022-01-16 - Updated a minor nit with the display of the moves list
 *                       and move counts (had one too many for games that were
 *                       complete - won/lost/tied).
 * 
 */
namespace Go_Moku
{
    public partial class MovesDlg : Form
    {
        #region Properties
        private Queue<string> _moves = null;
        public Queue<string> Moves { set => _moves = value; }

        private bool _gameWon = false;
        public bool GameWon { set => _gameWon = value; }
        #endregion

        // --------------------------------------------------------------------

        public MovesDlg()
        {
            InitializeComponent();
        }

        // --------------------------------------------------------------------

        #region Form events
        private void MovesDlg_Load(object sender, EventArgs e)
        {
            if (_moves != null)
            {
                StringBuilder sb = new StringBuilder();
                int cnt = 0, moveCnt = _moves.Count;

                if (_gameWon) moveCnt--;  // decrement (last move is win/loss/tied message)

                sb.Append("Move Count: " + moveCnt);
                foreach(string move in _moves)
                {
                    cnt++;
                    sb.AppendLine();
                    if (_gameWon && cnt >= _moves.Count)
                        sb.Append("xxx");
                    else
                        sb.Append(cnt.ToString("000"));
                    sb.Append(" => ");
                    sb.Append(move);
                }
                tbMovesList.Text = sb.ToString();
            }
        }
        #endregion
    }
}
