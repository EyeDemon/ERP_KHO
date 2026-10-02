import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft, Search } from 'lucide-react';
import { useMockDemo } from '../context/MockDemoContext';
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
          <span>ERP WMS • MOCK GLOBAL SEARCH</span>
          <h1>Tìm kiếm toàn hệ thống</h1>
          <p>Ưu tiên Product, exact Barcode, Document No., Lot/Serial và Business Partner. Operational results tôn trọng warehouse scope của persona mô phỏng.</p>
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
          aria-label="Global search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="SKU, barcode, chứng từ, lot, serial, partner..."
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
          <div className="global-search-empty">Không có dữ liệu phù hợp trong scope hiện tại.</div>
        ) : null}

        {results.map((result) => (
          <article key={result.id}>
            <div className="global-search-kind">{result.kind}</div>
            <div className="global-search-main">
              <strong>{result.reference}</strong>
              <h3>{result.title}</h3>
              <p>{result.detail}</p>
            </div>
            <div className="global-search-meta">
              {result.exact && <span className="exact-badge">EXACT</span>}
              {result.warehouse ? <span>{result.warehouse}</span> : null}
              {result.status ? <span>{result.status}</span> : null}
            </div>
          </article>
        ))}
      </section>
    </div>
  );
};

export default MockGlobalSearch;
