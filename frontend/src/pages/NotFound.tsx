import { Link } from 'react-router-dom';
import { UiCard, UiPage, UiPageHeader } from '../ui/ProductionUi';

const NotFound = () => {
  return (
    <main className="public-state production-ui">
      <UiPage>
        <UiPageHeader
          eyebrow="Điều hướng"
          title="404 - Không tìm thấy trang"
          description="Trang bạn yêu cầu không tồn tại hoặc đường dẫn đã thay đổi."
        />
        <UiCard title="404 - Không tìm thấy trang">
          <p>Trang bạn yêu cầu không tồn tại hoặc đường dẫn đã thay đổi.</p>
          <Link className="public-state-link" to="/">Quay về trang chủ</Link>
        </UiCard>
      </UiPage>
    </main>
  );
};

export default NotFound;
