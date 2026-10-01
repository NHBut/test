import React, { useState } from 'react';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { Navbar } from './components/Navbar';
import { FlashSaleBanner } from './components/FlashSaleBanner';
import { ProductCard, type Product } from './components/ProductCard';
import { CartDrawer } from './components/CartDrawer';
import { AuthModal } from './components/AuthModal';
import { OrderHistoryModal } from './components/OrderHistoryModal';
import { apiClient } from './api/client';
import { Sparkles, Layers, Cpu, ShieldCheck } from 'lucide-react';

const queryClient = new QueryClient();

const MainContent: React.FC = () => {
  const [isAuthOpen, setIsAuthOpen] = useState(false);
  const [isOrdersOpen, setIsOrdersOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');

  // Fetch sản phẩm từ Backend qua TanStack Query
  const { data: products, isLoading } = useQuery<Product[]>({
    queryKey: ['products'],
    queryFn: async () => {
      const res = await apiClient.get('/products?page=1&pageSize=20');
      return res.data || [];
    },
  });

  const filteredProducts = products?.filter((p) =>
    p.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
    (p.brand && p.brand.toLowerCase().includes(searchQuery.toLowerCase()))
  );

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 flex flex-col font-sans">
      <Navbar
        onOpenAuth={() => setIsAuthOpen(true)}
        onOpenOrders={() => setIsOrdersOpen(true)}
        searchQuery={searchQuery}
        setSearchQuery={setSearchQuery}
      />

      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-8">
        {/* Flash Sale Section */}
        <FlashSaleBanner
          skuId="71e6fd02-3410-4fac-a801-bf629ff1fc6a"
          skuName="iPhone 15 Pro Max (Natural Titanium 256GB)"
          price={29990000}
          originalPrice={34990000}
        />

        {/* Section Header */}
        <div className="flex items-center justify-between mb-6">
          <div>
            <h2 className="text-xl font-extrabold text-white flex items-center gap-2">
              <Sparkles className="w-5 h-5 text-amber-400" />
              Sản Phẩm Nổi Bật (Cache Aside via Redis)
            </h2>
            <p className="text-xs text-slate-400 mt-1">
              Phản hồi cực nhanh dưới 5ms qua tầng đệm L2 Cache và PostgreSQL Primary.
            </p>
          </div>
        </div>

        {/* Product Grid */}
        {isLoading ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-6">
            {[1, 2, 3, 4].map((n) => (
              <div key={n} className="h-80 rounded-2xl bg-slate-900/60 border border-slate-800 animate-pulse" />
            ))}
          </div>
        ) : filteredProducts && filteredProducts.length > 0 ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-6">
            {filteredProducts.map((product) => (
              <ProductCard key={product.id} product={product} />
            ))}
          </div>
        ) : (
          <div className="text-center py-20 bg-slate-900/40 rounded-2xl border border-slate-800/60">
            <p className="text-slate-400 text-sm">Không tìm thấy sản phẩm nào phù hợp với từ khóa.</p>
          </div>
        )}

        {/* System Architecture Highlights Cards */}
        <section className="mt-16 pt-12 border-t border-slate-800/80">
          <h3 className="text-lg font-bold text-center text-slate-200 mb-8">
            Nền Tảng Kiến Trúc Cốt Lõi Đang Vận Hành Dưới Nền
          </h3>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            <div className="p-6 rounded-2xl bg-slate-900 border border-slate-800 flex items-start gap-4">
              <div className="p-3 rounded-xl bg-indigo-500/10 text-indigo-400 border border-indigo-500/20">
                <Layers className="w-6 h-6" />
              </div>
              <div>
                <h4 className="font-bold text-sm text-slate-100">Modular Monolith</h4>
                <p className="text-xs text-slate-400 mt-1 leading-relaxed">
                  11 Bounded Contexts độc lập với 10 Schemas riêng biệt. Sẵn sàng tách Microservice mà không cần rewrite logic.
                </p>
              </div>
            </div>

            <div className="p-6 rounded-2xl bg-slate-900 border border-slate-800 flex items-start gap-4">
              <div className="p-3 rounded-xl bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                <Cpu className="w-6 h-6" />
              </div>
              <div>
                <h4 className="font-bold text-sm text-slate-100">Zero-Oversell Engine</h4>
                <p className="text-xs text-slate-400 mt-1 leading-relaxed">
                  Conditional Update nguyên tử tại PostgreSQL kết hợp Redis Lua script, loại bỏ 100% rủi ro bán vượt tồn kho.
                </p>
              </div>
            </div>

            <div className="p-6 rounded-2xl bg-slate-900 border border-slate-800 flex items-start gap-4">
              <div className="p-3 rounded-xl bg-pink-500/10 text-pink-400 border border-pink-500/20">
                <ShieldCheck className="w-6 h-6" />
              </div>
              <div>
                <h4 className="font-bold text-sm text-slate-100">Transactional Outbox</h4>
                <p className="text-xs text-slate-400 mt-1 leading-relaxed">
                  Triệt tiêu Dual-Write Problem. Đồng bộ dữ liệu bất đồng bộ lên Apache Kafka với cơ chế At-least-once delivery.
                </p>
              </div>
            </div>
          </div>
        </section>
      </main>

      {/* Footer */}
      <footer className="border-t border-slate-800/80 bg-slate-950 py-8 text-center text-xs text-slate-500">
        <p>© 2026 Nexus E-Commerce Engine. Production-Ready Enterprise Architecture.</p>
        <p className="mt-1">ASP.NET Core 9 • PostgreSQL • Redis • Kafka • OpenSearch • Kubernetes Ready</p>
      </footer>

      {/* Modals & Drawers */}
      <CartDrawer />
      <AuthModal isOpen={isAuthOpen} onClose={() => setIsAuthOpen(false)} />
      <OrderHistoryModal isOpen={isOrdersOpen} onClose={() => setIsOrdersOpen(false)} />
    </div>
  );
};

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <MainContent />
    </QueryClientProvider>
  );
}
