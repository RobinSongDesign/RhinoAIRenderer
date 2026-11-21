# Chính sách quyền riêng tư

~ Ngày hiệu lực: {{effective}}

Chính sách này giải thích cách đơn vị vận hành Chameleon là {{operator}} (sau đây gọi là "chúng tôi") thu thập, sử dụng, lưu trữ và bảo vệ thông tin cá nhân của bạn khi bạn sử dụng plug-in Chameleon cho Rhino, trang web rhchameleon.top và dịch vụ tài khoản api.rhchameleon.top (gọi chung là "Dịch vụ").

## 1. Thông tin chúng tôi thu thập

| Thông tin | Mục đích | Thời điểm |
|---|---|---|
| Tên người dùng, email, mật khẩu (lưu dạng băm; chúng tôi không thể xem) | Đăng ký, đăng nhập, khôi phục mật khẩu | Khi đăng ký và đăng nhập |
| Token (mỗi máy tính một token, tên chứa tên máy) | Cho phép plug-in dùng Dịch vụ thay bạn | Khi đăng nhập từ plug-in |
| Số dư, lịch sử nạp tiền và đổi mã | Tính phí, đối soát, hoàn tiền | Khi nạp tiền, đổi mã |
| Nhật ký sử dụng: thời gian, mô hình, kích thước ảnh, chi phí, tên token, địa chỉ IP | Tính phí, xử lý sự cố, chống lạm dụng, lưu trữ theo luật | Mỗi lần tạo ảnh hoặc tra số dư |
| Cookie phiên đăng nhập trang web | Duy trì đăng nhập trên trang web | Khi sử dụng trang web |

**Câu lệnh và hình ảnh**: ảnh nguồn, ảnh tham chiếu và câu lệnh bạn gửi khi tạo ảnh, cũng như ảnh được tạo, chỉ được dùng để hoàn thành lần tạo ảnh đó. Chúng đi qua máy chủ của chúng tôi tới nhà cung cấp mô hình; máy chủ của chúng tôi **không lưu** các nội dung này và không dùng chúng để huấn luyện mô hình.

Plug-in lưu lịch sử tạo ảnh và hình ảnh trên máy tính của bạn, lưu token trong chuỗi khóa hệ thống (Windows Data Protection, macOS Keychain). Các dữ liệu này không bao giờ được tải lên cho chúng tôi; gỡ cài đặt plug-in hoặc xóa thư mục dữ liệu của plug-in sẽ xóa chúng.

## 2. Cách chúng tôi sử dụng thông tin

- Cung cấp, duy trì và cải thiện Dịch vụ;
- Tính phí, đối soát, xử lý nạp tiền, đổi mã và hoàn tiền;
- Bảo đảm an toàn cho tài khoản và Dịch vụ, phát hiện yêu cầu bất thường và nội dung bị cấm;
- Thực hiện nghĩa vụ theo pháp luật và đáp ứng yêu cầu hợp pháp của cơ quan có thẩm quyền.

Chúng tôi không bán thông tin cá nhân của bạn và không dùng nó cho hoạt động tiếp thị không liên quan đến Dịch vụ.

## 3. Bên xử lý và bên thứ ba

Để tạo ảnh, câu lệnh và hình ảnh của bạn được chuyển tới các bên thứ ba sau, và được họ xử lý theo chính sách quyền riêng tư của riêng họ:

| Bên nhận | Dữ liệu | Mục đích |
|---|---|---|
| Nhà cung cấp API tạo ảnh (APIMart) | Câu lệnh, ảnh nguồn và ảnh tham chiếu, ảnh được tạo | Gọi mô hình; ảnh được tạo được lưu tạm trên máy chủ của họ và hết hạn sau 24 đến 72 giờ |
| Nhà cung cấp mô hình (Google Gemini) | Như trên | Tạo ảnh |
| Nhà cung cấp máy chủ đám mây (Tencent Cloud) | Toàn bộ dữ liệu trên máy chủ của chúng tôi | Lưu trữ Dịch vụ |

Ngoài các trường hợp trên và theo yêu cầu của pháp luật, chúng tôi không cung cấp thông tin cá nhân của bạn cho bên thứ ba khác khi chưa có sự đồng ý của bạn.

## 4. Nơi lưu trữ và chuyển dữ liệu ra nước ngoài

Máy chủ của chúng tôi đặt tại **Tokyo, Nhật Bản**, và các dịch vụ tạo ảnh nêu trên cũng nằm ngoài Trung Quốc đại lục. Việc sử dụng Dịch vụ đồng nghĩa với việc thông tin tài khoản, nhật ký sử dụng, cùng câu lệnh và hình ảnh bạn gửi để tạo ảnh sẽ được chuyển ra ngoài Trung Quốc đại lục để xử lý. Chúng tôi bảo vệ chúng bằng các biện pháp tại Mục 6. Nếu bạn không đồng ý với việc chuyển dữ liệu này, vui lòng không sử dụng Dịch vụ.

## 5. Thời hạn lưu trữ

- Thông tin tài khoản: cho đến khi bạn xóa tài khoản.
- Nhật ký sử dụng và lịch sử nạp tiền: ít nhất sáu tháng theo quy định của pháp luật, sau đó được xóa hoặc ẩn danh hóa.
- Câu lệnh và hình ảnh: không lưu trên máy chủ của chúng tôi.

## 6. Cách chúng tôi bảo vệ thông tin

- Mọi kết nối giữa trang web, plug-in và máy chủ đều được mã hóa bằng HTTPS;
- Mật khẩu được lưu dạng băm một chiều; plug-in không bao giờ lưu mật khẩu của bạn;
- Nhật ký máy chủ không ghi câu lệnh, hình ảnh và giá trị token;
- Chỉ những nhân sự cần thiết mới được truy cập máy chủ và trang quản trị.

Khi xảy ra sự cố an ninh liên quan đến thông tin cá nhân, chúng tôi sẽ thông báo cho bạn và báo cáo cơ quan có thẩm quyền theo quy định của pháp luật.

## 7. Quyền của bạn

- Xem và sửa thông tin tài khoản, nhật ký sử dụng và số dư trên trang web;
- Xóa bất kỳ token nào trong mục "Quản lý token" trên trang web, plug-in trên máy tính tương ứng sẽ ngừng hoạt động ngay;
- Liên hệ với chúng tôi để nhận bản sao, chỉnh sửa thông tin cá nhân, hoặc xóa tài khoản và thông tin cá nhân.

Chúng tôi xử lý yêu cầu trong vòng mười lăm ngày làm việc.

## 8. Trẻ vị thành niên

Dịch vụ dành cho người trưởng thành. Người dưới 18 tuổi chỉ nên sử dụng khi có sự đồng ý và hướng dẫn của người giám hộ. Chúng tôi không cố ý thu thập thông tin cá nhân của trẻ em dưới 14 tuổi và sẽ xóa ngay khi phát hiện.

## 9. Cập nhật

Các bản cập nhật chính sách được công bố trên trang web; những thay đổi quan trọng về mục đích, phương thức hoặc phạm vi xử lý sẽ được thông báo trên trang web. Nếu có khác biệt giữa các phiên bản ngôn ngữ, bản tiếng Trung giản thể được ưu tiên áp dụng.

## 10. Liên hệ

Câu hỏi về chính sách này hoặc thông tin cá nhân của bạn: {{email}}
