import React, { useState } from 'react';
import { X, Trash2, ShoppingBag, ArrowRight, Tag, CheckCircle2 } from 'lucide-react';
import { useCartStore } from '../store/useCartStore';
import { useAuthStore } from '../store/useAuthStore';
import { apiClient } from '../api/client';

export const CartDrawer: React.FC = () => {
  const { items, isOpen, closeCart, removeItem, clearCart } = useCartStore();
  const { user } = useAuthStore();
  const [couponCode, setCouponCode] = useState('');
  const [discount, setDiscount] = useState(0);
  const [couponMsg, setCouponMsg] = useState('');
  const [isOrdering, setIsOrdering] = useState(false);
  const [orderSuccess, setOrderSuccess] = useState<string | null>(null);

  if (!isOpen) return null;

  const subtotal = items.reduce((sum, i) => sum + i.unitPrice * i.quantity, 0);
  const total = Math.max(0, subtotal - discount);

  const handleApplyCoupon = async () => {
    if (!couponCode.trim()) return;
    try {
      const res = await apiClient.post('/promotions/apply', {
        code: couponCode,
        subtotal,
      });
      if (res.data?.isValid) {
        setDiscount(res.data.discountAmount);
        setCouponMsg(res.data.message);
      }
    } catch (err: any) {
      setCouponMsg(err.response?.data?.message || 'Mã giảm giá không hợp lệ');
    }
  };

  const handleCheckout = async () => {
    if (items.length === 0) return;
    setIsOrdering(true);
    setOrderSuccess(null);

    const customerId = user?.id || 'f121f4f9-37cc-4c0b-b322-368a27755978';
    const idempotencyKey = `ord-${Date.now()}-${Math.random().toString(36).substring(7)}`;

    try {
      const res = await apiClient.post(
        '/orders',
        {
          customerId,
          shippingAddressJson: JSON.stringify({ street: '123 Tech Park', city: 'Ho Chi Minh' }),
          items: items.map((i) => ({
            skuId: i.skuId,
            sku: i.sku,
            productName: i.productName,
            variantName: 'Default',
            unitPrice: i.unitPrice,
            quantity: i.quantity,
          })),
          discountAmount: discount,
          shippingFee: 30000,
        },
        {
          headers: {
            'Idempotency-Key': idempotencyKey,
          },
        }
      );

      if (res.status === 201 || res.status === 200) {
        setOrderSuccess(res.data?.orderId || 'Thành công');
        clearCart(customerId);
      }
    } catch (err: any) {
      alert('Đặt hàng thất bại: ' + (err.response?.data?.detail || err.message));
    } finally {
      setIsOrdering(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 overflow-hidden bg-black/60 backdrop-blur-sm animate-fade-in">
      <div className="absolute inset-y-0 right-0 max-w-full flex pl-10">
        <div className="w-screen max-w-md bg-slate-900 border-l border-slate-800 text-white flex flex-col shadow-2xl">
          {/* Header */}
          <div className="p-6 border-b border-slate-800 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <ShoppingBag className="w-5 h-5 text-indigo-400" />
              <h2 className="text-lg font-bold">Giỏ Hàng (Redis Store)</h2>
            </div>
            <button
              onClick={closeCart}
              className="p-2 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition-colors"
            >
              <X className="w-5 h-5" />
            </button>
          </div>

          {/* Body */}
          <div className="flex-1 overflow-y-auto p-6 space-y-4">
            {orderSuccess ? (
              <div className="text-center py-12 space-y-4">
                <div className="w-16 h-16 bg-emerald-500/20 text-emerald-400 rounded-full flex items-center justify-center mx-auto border border-emerald-500/30">
                  <CheckCircle2 className="w-8 h-8" />
                </div>
                <h3 className="text-xl font-bold text-slate-100">Đặt hàng thành công!</h3>
                <p className="text-xs text-slate-400 max-w-xs mx-auto">
                  Mã đơn hàng đã được đẩy vào Transactional Outbox và chuẩn bị phân phối qua Apache Kafka.
                </p>
                <div className="p-3 bg-slate-800/80 rounded-xl font-mono text-xs text-indigo-300 break-all">
                  OrderId: {orderSuccess}
                </div>
                <button
                  onClick={() => {
                    setOrderSuccess(null);
                    closeCart();
                  }}
                  className="px-6 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-bold transition-all"
                >
                  Tiếp tục mua sắm
                </button>
              </div>
            ) : items.length === 0 ? (
              <div className="text-center py-16 text-slate-500">
                <ShoppingBag className="w-12 h-12 mx-auto stroke-[1.5] mb-2 opacity-50" />
                <p className="text-sm">Giỏ hàng của bạn đang trống.</p>
              </div>
            ) : (
              items.map((item) => (
                <div
                  key={item.skuId}
                  className="p-4 rounded-xl bg-slate-800/60 border border-slate-700/60 flex items-center justify-between gap-3"
                >
                  <div className="flex-1 min-w-0">
                    <h4 className="font-semibold text-sm text-slate-200 truncate">{item.productName}</h4>
                    <div className="text-xs text-slate-400 mt-0.5 font-mono">{item.sku}</div>
                    <div className="text-sm font-bold text-indigo-400 mt-1">
                      {item.unitPrice.toLocaleString('vi-VN')} đ × {item.quantity}
                    </div>
                  </div>
                  <button
                    onClick={() => removeItem(user?.id || 'guest-user', item.skuId)}
                    className="p-2 rounded-lg text-slate-400 hover:text-red-400 hover:bg-red-500/10 transition-colors"
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
              ))
            )}
          </div>

          {/* Footer */}
          {!orderSuccess && items.length > 0 && (
            <div className="p-6 border-t border-slate-800 space-y-4 bg-slate-900/90">
              {/* Coupon */}
              <div className="flex gap-2">
                <div className="relative flex-1">
                  <input
                    type="text"
                    placeholder="Mã giảm giá (ví dụ: SAVE20)"
                    value={couponCode}
                    onChange={(e) => setCouponCode(e.target.value)}
                    className="w-full bg-slate-800 border border-slate-700 rounded-xl py-2 pl-9 pr-3 text-xs text-slate-200 uppercase focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  />
                  <Tag className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
                </div>
                <button
                  onClick={handleApplyCoupon}
                  className="px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 border border-slate-700 text-xs font-semibold text-slate-200"
                >
                  Áp dụng
                </button>
              </div>
              {couponMsg && <div className="text-[11px] text-indigo-400">{couponMsg}</div>}

              {/* Totals */}
              <div className="space-y-1.5 text-xs text-slate-400">
                <div className="flex justify-between">
                  <span>Tạm tính:</span>
                  <span className="font-semibold text-slate-200">{subtotal.toLocaleString('vi-VN')} đ</span>
                </div>
                {discount > 0 && (
                  <div className="flex justify-between text-emerald-400 font-semibold">
                    <span>Giảm giá coupon:</span>
                    <span>-{discount.toLocaleString('vi-VN')} đ</span>
                  </div>
                )}
                <div className="flex justify-between text-base font-extrabold text-white pt-2 border-t border-slate-800">
                  <span>Tổng thanh toán:</span>
                  <span className="text-indigo-400">{total.toLocaleString('vi-VN')} đ</span>
                </div>
              </div>

              {/* Checkout Button */}
              <button
                onClick={handleCheckout}
                disabled={isOrdering}
                className="w-full py-3.5 rounded-xl bg-gradient-to-r from-indigo-600 to-purple-600 hover:from-indigo-500 hover:to-purple-500 text-white font-bold text-sm shadow-xl shadow-indigo-600/30 flex items-center justify-center gap-2 hover:scale-[1.02] active:scale-[0.98] transition-all disabled:opacity-50"
              >
                {isOrdering ? 'Đang tạo đơn hàng & Outbox...' : 'XÁC NHẬN ĐẶT HÀNG'}
                <ArrowRight className="w-4 h-4" />
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
