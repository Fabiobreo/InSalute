﻿using InSalute.Models;
using System;

namespace InSalute.Stores
{
    public class UserStore
    {
        private UserExtended _currentUser;
        public UserExtended CurrentUser
        {
            get => _currentUser;
            set
            {
                _currentUser = value;
                CurrentUserChanged?.Invoke();
            }
        }

        public bool IsLoggedIn => CurrentUser != null;

        public event Action CurrentUserChanged;
        public event Action CurrentUserUpdated;

        public void Update()
        {
            CurrentUserUpdated?.Invoke();
        }

        public void Logout()
        {
            CurrentUser = null;
        }
    }
}
