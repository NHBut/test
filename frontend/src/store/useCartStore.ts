import { create } from 'zustand';
import { apiClient } from '../api/client';

export interface CartItem {
  skuId: string;
  sku: string;
  productName: string;
  unitPrice: number;
  quantity: number;
}

interface CartState {
  items: CartItem[];
  isOpen: boolean;
  openCart: () => void;
  closeCart: () => void;
  addItem: (userId: string, item: Omit<CartItem, 'quantity'> & { quantity?: number }) => Promise<void>;
  removeItem: (userId: string, skuId: string) => Promise<void>;
  fetchCart: (userId: string) => Promise<void>;
  clearCart: (userId: string) => Promise<void>;
}

export const useCartStore = create<CartState>((set, get) => ({
  items: [],
  isOpen: false,
  openCart: () => set({ isOpen: true }),
  closeCart: () => set({ isOpen: false }),

  fetchCart: async (userId: string) => {
    try {
      const res = await apiClient.get(`/cart/${userId}`);
      if (res.data?.items) {
        set({ items: res.data.items });
      }
    } catch (err) {
      console.error('Failed to fetch cart', err);
    }
  },

  addItem: async (userId: string, item) => {
    const qty = item.quantity || 1;
    // Cập nhật optimistic local
    const current = get().items;
    const exists = current.find((x) => x.skuId === item.skuId);
    let updated: CartItem[];
    if (exists) {
      updated = current.map((x) => (x.skuId === item.skuId ? { ...x, quantity: x.quantity + qty } : x));
    } else {
      updated = [...current, { ...item, quantity: qty }];
    }
    set({ items: updated, isOpen: true });

    // Gọi API Redis backend
    try {
      await apiClient.post('/cart/items', {
        userId,
        skuId: item.skuId,
        sku: item.sku,
        productName: item.productName,
        unitPrice: item.unitPrice,
        quantity: qty,
      });
    } catch (err) {
      console.error('Failed to sync cart to Redis', err);
    }
  },

  removeItem: async (userId: string, skuId: string) => {
    set({ items: get().items.filter((x) => x.skuId !== skuId) });
    try {
      await apiClient.delete(`/cart/${userId}/items/${skuId}`);
    } catch (err) {
      console.error('Failed to remove cart item', err);
    }
  },

  clearCart: async (userId: string) => {
    set({ items: [] });
    try {
      await apiClient.delete(`/cart/${userId}`);
    } catch (err) {
      console.error('Failed to clear cart', err);
    }
  },
}));
