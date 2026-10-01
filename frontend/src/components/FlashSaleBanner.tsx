import React, { useState } from 'react';
import { Flame, Zap, CheckCircle2, AlertCircle } from 'lucide-react';
import { apiClient } from '../api/client';

interface FlashSaleBannerProps {
  skuId: string;
  skuName: string;
  price: number;
  originalPrice: number;
}

export const FlashSaleBanner: React.FC<FlashSaleBannerProps> = ({ skuId, skuName, price, originalPrice }) => {
  const [stock, setStock] = useState(100);
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState<{ text: string; type: 'success' | 'error' } | null>(null);

  const handleInstantBuy = async () => {
    setLoading(true);
    setMessage(null);

    const idempotencyKey = `flash-${Date.now()}-${Math.random().toString(36).substring(7)}`;
    const orderId = '00000000-0000-4000-8000-' + Math.floor(Math.random() * 1000000000000).toString().padStart(12, '0');

    try {
      const res = await apiClient.post(
        '/inventory/reserve',
        {
          orderId,
          items: [{ skuId, quantity: 1 }],
          ttlMinutes: 15,
        },
        {
          headers: {
            'Idempotency-Key': idempotencyKey,
          },
        }
      );

      if (res.status === 200) {
        setStock((prev) => Math.max(0, prev - 1));
        setMessage({
          text: 'Giữ hàng thành công! Đơn hàng được bảo lưu trong 15 phút (Zero Oversell).',
          type: 'success',
        });
      }
    } catch (err: any) {
      if (err.response?.status === 409) {
        setMessage({
          text: 'RẤT TIẾC! Sản phẩm đã cháy hàng (Out of Stock).',
          type: 'error',
        });
      } else {
        setMessage({
          text: 'Giao dịch thất bại. Vui lòng thử lại.',
          type: 'error',
        });
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="relative overflow-hidden rounded-2xl bg-gradient-to-r from-red-600 via-rose-600 to-pink-600 p-6 sm:p-8 text-white shadow-2xl shadow-rose-600/20 mb-8 border border-rose-500/30">
      <div className="absolute -right-10 -bottom-10 opacity-10 pointer-events-none">
        <Flame className="w-64 h-64 text-white" />
      </div>

      <div className="flex flex-col md:flex-row md:items-center justify-between gap-6 relative z-10">
        <div>
          <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-white/20 backdrop-blur-md text-xs font-bold tracking-wider uppercase mb-3">
            <Flame className="w-4 h-4 text-amber-300 animate-bounce" />
            Mega Flash Sale 100,000 CCU Ready
          </div>
          <h2 className="text-2xl sm:text-3xl font-black tracking-tight">{skuName}</h2>
          <p className="text-white/80 text-sm mt-1">
            Xử lý hàng chục nghìn lượt mua đồng thời với thuật toán <strong>Atomic Conditional Update</strong> & <strong>Idempotency-Key</strong>.
          </p>

          <div className="flex items-baseline gap-3 mt-4">
            <span className="text-3xl sm:text-4xl font-black text-amber-300">
              {price.toLocaleString('vi-VN')} đ
            </span>
            <span className="text-lg line-through text-white/60">
              {originalPrice.toLocaleString('vi-VN')} đ
            </span>
            <span className="px-2 py-0.5 rounded-md bg-white text-rose-600 font-extrabold text-xs">
              -15%
            </span>
          </div>

          {/* Stock bar */}
          <div className="mt-4 max-w-xs">
            <div className="flex justify-between text-xs font-semibold mb-1 text-white/90">
              <span>Tồn kho khả dụng:</span>
              <span>{stock} / 100 chiếc</span>
            </div>
            <div className="w-full bg-black/30 rounded-full h-2.5 overflow-hidden">
              <div
                className="bg-amber-400 h-2.5 rounded-full transition-all duration-500"
                style={{ width: `${stock}%` }}
              />
            </div>
          </div>
        </div>

        {/* Action Button */}
        <div className="flex flex-col items-start md:items-end gap-3">
          <button
            onClick={handleInstantBuy}
            disabled={loading || stock <= 0}
            className="w-full sm:w-auto px-8 py-4 rounded-xl bg-white text-rose-600 hover:bg-amber-300 hover:text-slate-900 font-black text-base shadow-xl shadow-black/20 hover:scale-105 active:scale-95 transition-all flex items-center justify-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <Zap className="w-5 h-5 fill-current" />
            {loading ? 'Đang tranh mua...' : stock > 0 ? 'TRANH MUA NGAY (1-CLICK)' : 'HẾT HÀNG'}
          </button>

          {message && (
            <div
              className={`flex items-center gap-2 text-xs font-medium px-3 py-2 rounded-lg backdrop-blur-md ${
                message.type === 'success'
                  ? 'bg-emerald-950/60 text-emerald-200 border border-emerald-500/40'
                  : 'bg-black/60 text-amber-200 border border-amber-500/40'
              }`}
            >
              {message.type === 'success' ? (
                <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
              ) : (
                <AlertCircle className="w-4 h-4 text-amber-400 shrink-0" />
              )}
              <span>{message.text}</span>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
