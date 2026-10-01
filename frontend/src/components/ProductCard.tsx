import React from 'react';
import { ShoppingCart, Star } from 'lucide-react';
import { useCartStore } from '../store/useCartStore';
import { useAuthStore } from '../store/useAuthStore';

export interface ProductVariant {
  id: string;
  sku: string;
  variantName: string;
  price: number;
  compareAtPrice?: number;
}

export interface Product {
  id: string;
  name: string;
  slug: string;
  brand?: string;
  description?: string;
  variants: ProductVariant[];
}

interface ProductCardProps {
  product: Product;
}

export const ProductCard: React.FC<ProductCardProps> = ({ product }) => {
  const { addItem } = useCartStore();
  const { user } = useAuthStore();
  const defaultVariant = product.variants[0];

  const handleAddToCart = () => {
    if (!defaultVariant) return;
    const userId = user?.id || 'guest-user-00000000-0000';
    addItem(userId, {
      skuId: defaultVariant.id,
      sku: defaultVariant.sku,
      productName: product.name,
      unitPrice: defaultVariant.price,
      quantity: 1,
    });
  };

  return (
    <div className="group rounded-2xl bg-slate-900 border border-slate-800 hover:border-indigo-500/50 p-5 transition-all duration-300 hover:shadow-xl hover:shadow-indigo-500/10 flex flex-col justify-between">
      <div>
        {/* Thumbnail Placeholder with Gradient */}
        <div className="w-full h-48 rounded-xl bg-gradient-to-br from-slate-800 to-slate-950 flex items-center justify-center relative overflow-hidden group-hover:scale-[1.02] transition-transform">
          <div className="text-4xl">📱</div>
          {product.brand && (
            <span className="absolute top-3 left-3 px-2 py-1 rounded-md bg-slate-900/80 backdrop-blur-md text-[10px] font-bold text-slate-300 border border-slate-700">
              {product.brand}
            </span>
          )}
        </div>

        {/* Info */}
        <div className="mt-4">
          <div className="flex items-center gap-1 text-amber-400 text-xs">
            <Star className="w-3.5 h-3.5 fill-current" />
            <span className="font-semibold text-slate-200">4.9</span>
            <span className="text-slate-500">(1,240 đánh giá)</span>
          </div>
          <h3 className="font-bold text-base text-slate-100 mt-1 line-clamp-1 group-hover:text-indigo-400 transition-colors">
            {product.name}
          </h3>
          <p className="text-slate-400 text-xs mt-1 line-clamp-2">
            {product.description || 'Sản phẩm chính hãng với bảo hành toàn diện.'}
          </p>
        </div>
      </div>

      {/* Pricing & Add to Cart */}
      <div className="mt-5 pt-4 border-t border-slate-800/80 flex items-center justify-between">
        <div>
          <div className="text-xs text-slate-400">Giá tốt nhất:</div>
          <div className="text-lg font-black text-slate-100">
            {defaultVariant ? defaultVariant.price.toLocaleString('vi-VN') + ' đ' : 'Liên hệ'}
          </div>
        </div>

        <button
          onClick={handleAddToCart}
          className="p-3 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white shadow-lg shadow-indigo-600/30 hover:scale-105 active:scale-95 transition-all"
          title="Thêm vào giỏ hàng (Redis Sync)"
        >
          <ShoppingCart className="w-4 h-4" />
        </button>
      </div>
    </div>
  );
};
