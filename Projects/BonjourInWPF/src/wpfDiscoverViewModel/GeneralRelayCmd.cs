// Copyright (C) 2024 Bharath K A (bkumarbk9)
// SPDX-License-Identifier: AGPL-3.0-only
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License version 3
// as published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Windows.Input;

namespace wpfDiscoverViewModel
{
    /// <summary>
    /// General relay command for binding View events to Model actions
    /// </summary>
    internal class GeneralRelayCmd : ICommand
    {
        private readonly Action<object> action;

        public GeneralRelayCmd(Action<object> actionParam)
        {
            action = actionParam ?? throw new ArgumentNullException(nameof(actionParam));
        }

        #region ICommand Members
        public bool CanExecute(object parameter)
        {
            return true;
        }

        // The command can always execute, so this event never needs to be raised
        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public void Execute(object parameter)
        {
            action(parameter);
        }
        #endregion // ICommand Members
    }
}
