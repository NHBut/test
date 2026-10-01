import React from 'react';
import { ShoppingBag, Search, User as UserIcon, LogOut, Zap, ShieldCheck } from 'lucide-react';
import { useCartStore } from '../store/useCartStore';
import { useAuthStore } from '../store/useAuthStore';

interface NavbarProps {
  onOpenAuth: () => void;
  onOpenOrders: () => void;
  searchQuery: string;
  setSearchQuery: (q: string) => void;
}

export const Navbar: React.FC<NavbarProps> = ({ onOpenAuth, onOpenOrders, searchQuery, setSearchQuery }) => {
  const { items, openCart } = useCartStore();
  const { user, logout } = useAuthStore();
  const totalItems = items.reduce((sum, item) => sum + item.quantity, 0);

  return (
    <header className="sticky top-0 z-40 bg-slate-900/90 backdrop-blur-md border-b border-slate-800 text-white">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between gap-4">
        {/* Logo */}
        <div className="flex items-center gap-2 cursor-pointer">
          <div className="w-10 h-10 rounded-xl bg-gradient-to-tr from-indigo-500 via-purple-500 to-pink-500 flex items-center justify-center shadow-lg shadow-indigo-500/30">
            <Zap className="w-6 h-6 text-white" />
          </div>
          <div>
            <span className="font-extrabold text-xl tracking-tight bg-gradient-to-r from-white via-slate-200 to-indigo-300 bg-clip-text text-transparent">
              NEXUS
            </span>
            <span className="text-xs ml-1 px-1.5 py-0.5 rounded bg-indigo-500/20 text-indigo-400 font-mono border border-indigo-500/30">
              SCALE 100K
            </span>
          </div>
        </div>

        {/* Search Input */}
        <div className="flex-1 max-w-md relative hidden sm:block">
          <input
            type="text"
            placeholder="Tìm kiếm sản phẩm, danh mục, thương hiệu..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full bg-slate-800/80 border border-slate-700/80 rounded-full py-2 pl-10 pr-4 text-sm text-slate-200 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent transition-all"
          />
          <Search className="w-4 h-4 text-slate-400 absolute left-3.5 top-2.5" />
        </div>

        {/* Actions */}
        <div className="flex items-center gap-3">
          {user ? (
            <div className="flex items-center gap-2">
              <button
                onClick={onOpenOrders}
                className="px-3 py-1.5 text-xs font-medium rounded-lg bg-slate-800 border border-slate-700 hover:bg-slate-700 text-slate-300 transition-colors flex items-center gap-1.5"
              >
                <ShieldCheck className="w-3.5 h-3.5 text-emerald-400" />
                Đơn hàng
              </button>
              <div className="text-right hidden md:block">
                <div className="text-xs font-semibold text-slate-200 truncate max-w-[120px]">{user.fullName}</div>
                <div className="text-[10px] text-slate-400">{user.email}</div>
              </div>
              <button
                onClick={logout}
                title="Đăng xuất"
                className="p-2 rounded-lg bg-slate-800 hover:bg-red-500/20 text-slate-400 hover:text-red-400 transition-colors"
              >
                <LogOut className="w-4 h-4" />
              </button>
            </div>
          ) : (
            <button
              onClick={onOpenAuth}
              className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white shadow-md shadow-indigo-600/30 transition-all"
            >
              <UserIcon className="w-3.5 h-3.5" />
              Đăng nhập / Đăng ký
            </button>
          )}

          {/* Cart Button */}
          <button
            onClick={openCart}
            className="relative p-2 rounded-xl bg-slate-800 hover:bg-slate-700 border border-slate-700 transition-all text-slate-200 hover:text-white"
          >
            <ShoppingBag className="w-5 h-5" />
            {totalItems > 0 && (
              <span className="absolute -top-1 -right-1 bg-gradient-to-r from-pink-500 to-rose-500 text-white font-bold text-[10px] w-5 h-5 rounded-full flex items-center justify-center shadow-lg shadow-pink-500/50 animate-pulse">
                {totalItems}
              </span>
            )}
          </button>
        </div>
      </div>
    </header>
  );
};
