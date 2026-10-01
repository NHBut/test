import { create } from 'zustand';

interface User {
  id: string;
  email: string;
  fullName: string;
}

interface AuthState {
  token: string | null;
  user: User | null;
  setAuth: (token: string, user: User) => void;
  logout: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  token: localStorage.getItem('access_token'),
  user: localStorage.getItem('user_info') ? JSON.parse(localStorage.getItem('user_info')!) : null,
  setAuth: (token, user) => {
    localStorage.setItem('access_token', token);
    localStorage.setItem('user_info', JSON.stringify(user));
    set({ token, user });
  },
  logout: () => {
    localStorage.removeItem('access_token');
    localStorage.removeItem('user_info');
    set({ token: null, user: null });
  },
}));
