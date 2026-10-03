import { Link } from 'react-router-dom';
import { UiCard, UiPage } from '../ui/ProductionUi';

const NotFound = () => {
  return (
    <main className="public-state production-ui">
      <UiPage>
        <UiCard title="404 - Không tìm thấy trang">
          <p>Trang bạn yêu cầu không tồn tại hoặc đường dẫn đã thay đổi.</p>
          <Link className="public-state-link" to="/">Quay về trang chủ</Link>
        </UiCard>
      </UiPage>
    </main>
  );
};

export default NotFound;
