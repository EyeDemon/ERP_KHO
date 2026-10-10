import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft, Search } from 'lucide-react';
import { useMockDemo } from '../context/MockDemoContext';
import { mockDisplayText, mockStatusLabel, mockTypeLabel } from '../utils/mockDisplayLabels';
import { searchMockSystem } from '../mocks/erpWmsMockSearch';
import './MockGlobalSearch.css';

const examples = ['8938501000011', 'GR-2026-1048', 'LOT-1001-260930', 'SER-TWS-000128', 'SUP-001'];

const MockGlobalSearch = () => {
  const demo = useMockDemo();
  const [query, setQuery] = useState('');
  const results = useMemo(
    () => searchMockSystem(query, demo.allowedWarehouses),
    [demo.allowedWarehouses, query],
  );

  return (
    <div className="global-search-page">
      <Link to="/system-blueprint" className="global-search-back"><ArrowLeft size={16} /> Bản đồ hệ thống</Link>
      <section className="global-search-hero">
        <div>
          <span>ERP WMS • TÌM KIẾM MÔ PHỎNG TOÀN HỆ THỐNG</span>
          <h1>Tìm kiếm toàn hệ thống</h1>
          <p>Ưu tiên Sản phẩm, Mã vạch khớp chính xác, Số chứng từ, Lô/Sê-ri và Đối tác. Kết quả vận hành tôn trọng phạm vi kho của vai trò mô phỏng.</p>
        </div>
        <div className="global-search-scope">
          <strong>{demo.selectedUser.name}</strong>
          <span>{demo.selectedUser.role}</span>
          <small>{demo.allowedWarehouses.join(', ')}</small>
        </div>
      </section>

      <section className="global-search-box">
        <Search size={20} />
        <input
          aria-label="Tìm kiếm toàn hệ thống"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="SKU, mã vạch, chứng từ, lô, sê-ri, đối tác..."
        />
        {query.length > 0 ? <button type="button" onClick={() => setQuery('')}>Xóa</button> : null}
      </section>

      <div className="global-search-examples">
        <span>Thử nhanh:</span>
        {examples.map((example) => <button key={example} type="button" onClick={() => setQuery(example)}>{example}</button>)}
      </div>

      <section className="global-search-results">
        <header>
          <h2>Kết quả</h2>
          <span>{query ? results.length + ' kết quả' : 'Nhập từ khóa để tìm'}</span>
        </header>

        {query.length > 0 && results.length === 0 ? (
          <div className="global-search-empty">Không có dữ liệu phù hợp trong phạm vi hiện tại.</div>
        ) : null}

        {results.map((result) => (
          <article key={result.id}>
            <div className="global-search-kind">{mockTypeLabel(result.kind)}</div>
            <div className="global-search-main">
              <strong>{result.reference}</strong>
              <h3>{mockDisplayText(result.title)}</h3>
              <p>{mockDisplayText(result.detail)}</p>
            </div>
            <div className="global-search-meta">
              {result.exact && <span className="exact-badge">KHỚP CHÍNH XÁC</span>}
              {result.warehouse ? <span>{result.warehouse}</span> : null}
              {result.status ? <span>{mockStatusLabel(result.status)}</span> : null}
            </div>
          </article>
        ))}
      </section>
    </div>
  );
};

export default MockGlobalSearch;
