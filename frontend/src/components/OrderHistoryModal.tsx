import React, { useEffect, useState } from 'react';
import { X, Package } from 'lucide-react';
import { apiClient } from '../api/client';
import { useAuthStore } from '../store/useAuthStore';

interface OrderHistoryModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export const OrderHistoryModal: React.FC<OrderHistoryModalProps> = ({ isOpen, onClose }) => {
  const { user } = useAuthStore();
  const [orders, setOrders] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (isOpen && user?.id) {
      setLoading(true);
      apiClient
        .get(`/orders/customer/${user.id}`)
        .then((res) => setOrders(res.data || []))
        .catch((err) => console.error(err))
        .finally(() => setLoading(false));
    }
  }, [isOpen, user?.id]);

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm animate-fade-in">
      <div className="w-full max-w-2xl bg-slate-900 border border-slate-800 rounded-2xl shadow-2xl overflow-hidden text-white flex flex-col max-h-[85vh]">
        {/* Header */}
        <div className="p-6 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Package className="w-5 h-5 text-indigo-400" />
            <h3 className="text-lg font-bold">Lịch Sử Đơn Hàng (Deterministic State Machine)</h3>
          </div>
          <button onClick={onClose} className="p-1 rounded-lg hover:bg-slate-800 text-slate-400 hover:text-white">
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* List */}
        <div className="p-6 overflow-y-auto space-y-4 flex-1">
          {loading ? (
            <div className="text-center py-12 text-slate-400 text-sm">Đang tải lịch sử đơn hàng...</div>
          ) : orders.length === 0 ? (
            <div className="text-center py-12 text-slate-500 text-sm">Bạn chưa có đơn hàng nào.</div>
          ) : (
            orders.map((order) => (
              <div
                key={order.id}
                className="p-5 rounded-xl bg-slate-800/60 border border-slate-700/60 space-y-3"
              >
                <div className="flex items-center justify-between">
                  <div>
                    <span className="font-mono font-bold text-sm text-indigo-400">{order.orderNumber}</span>
                    <div className="text-xs text-slate-400 mt-0.5">
                      {new Date(order.createdAt).toLocaleString('vi-VN')}
                    </div>
                  </div>
                  <span
                    className={`px-2.5 py-1 rounded-full text-xs font-bold ${
                      order.status === 'PAID'
                        ? 'bg-emerald-500/20 text-emerald-400 border border-emerald-500/30'
                        : order.status === 'PENDING_PAYMENT'
                        ? 'bg-amber-500/20 text-amber-400 border border-amber-500/30'
                        : 'bg-indigo-500/20 text-indigo-400 border border-indigo-500/30'
                    }`}
                  >
                    {order.status}
                  </span>
                </div>

                {/* Items */}
                <div className="divide-y divide-slate-700/40 border-y border-slate-700/40 py-2">
                  {order.items?.map((item: any) => (
                    <div key={item.id} className="py-1.5 flex justify-between text-xs">
                      <span className="text-slate-200">
                        {item.productName} ({item.variantName}) × {item.quantity}
                      </span>
                      <span className="font-semibold text-slate-300">
                        {item.totalPrice?.toLocaleString('vi-VN')} đ
                      </span>
                    </div>
                  ))}
                </div>

                {/* Total */}
                <div className="flex justify-between items-center text-sm pt-1">
                  <span className="text-slate-400 text-xs">Tổng cộng:</span>
                  <span className="font-black text-indigo-400 text-base">
                    {order.totalAmount?.toLocaleString('vi-VN')} đ
                  </span>
                </div>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
};
