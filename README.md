# Groundwork Upstream

DWG 설계 객체와 현장 측량 데이터를 연결하기 위한 로컬 분석 기반입니다.
현재 구현은 **DWG 구조 추출 CLI + 사용자 분류·규격 JSON 검증**입니다.
웹 업로드, 화면 뷰어, AI 객체 분류, 도곽 자동 탐지, LiDAR/3DGS 비교는 아직 구현하지 않았습니다.

## 실행

.NET 10 SDK가 필요합니다. DWG 파서는 MIT 라이선스의 ACadSharp **3.8.0**을 사용합니다.
초기 NuGet 복원에는 인터넷이 필요하며, 이후 DWG 처리는 로컬에서 수행합니다.
원본 DWG를 수정하거나 서버에 전송하지 않습니다.

```powershell
dotnet build src/Groundwork.Cad
# 출력 폴더는 먼저 생성합니다. 기존 출력 파일은 덮어쓰지 않습니다.
dotnet run --project src/Groundwork.Cad -- inspect "C:\drawings\sample.dwg" "C:\drawings\sample.index.json"
dotnet run --project src/Groundwork.Cad -- review-template "C:\drawings\sample.index.json" "C:\drawings\sample.review.json"
dotnet run --project src/Groundwork.Cad -- validate-review "C:\drawings\sample.index.json" "C:\drawings\sample.review.json"
```

`review-template`은 빈 objects 목록을 만듭니다. index의 entity ID를 복사하여 다음처럼 입력합니다.
클래스명은 사용자 정의 문자열이며 모델 예측값이 아닙니다. 치수는 사용자가 입력한 명세이며 측정값이 아닙니다.

```json
{
  "schemaVersion": "0.1.0",
  "sourceSha256": "index.source.sha256 값을 복사",
  "objects": [
    {
      "entityId": "index.entities에서 선택한 id",
      "className": "column",
      "confirmed": true,
      "dimensions": {
        "width": { "value": 400, "unit": "mm", "tolerance": 10 },
        "height": { "value": 3000, "unit": "mm" }
      }
    }
  ]
}
```

허용 단위는 mm/cm/m/in/ft이며 tolerance는 같은 단위의 비음수 값입니다.
다른 DWG 버전의 검토 파일, 존재하지 않는 객체, 중복 객체, 잘못된 단위·치수는 거부합니다.

## 추출 결과

- `source`: 파일명, 실제 읽은 파일의 SHA256, DWG 버전, 원본 단위
- `layers`: 원본 레이어명, on/off, flags, 색상, 선 종류
- `blocks`: 블록 정의, 기준점, 단위, 외부 참조 경로, 소속 객체 ID
- `entities`: 원본 handle, 소속 블록·레이어, 객체 종류, 형상 매개변수, 미분류 상태
- `pages`: 배치별 루트 객체와 참조 레이어. Model은 미분할 공간으로 유지
- `warnings`: 파서 알림과 지원하지 않는 형상 등 처리 한계

현재 형상 내보내기는 LINE, ARC, CIRCLE, LWPOLYLINE, INSERT, TEXT 계열, MTEXT의 일부 매개변수를 지원합니다.
그 밖의 객체는 metadata와 `geometryStatus: unsupported`로 남습니다. 모든 CAD 속성을 보존하는 무손실 변환은 아닙니다.

좌표는 객체별 원본 WCS/OCS 의미를 유지하며 각도는 라디안입니다. 블록을 월드 좌표로 펼치지 않습니다.
뷰포트 clipping/가시성, 레이어 0의 상속, 외부 참조 로딩, 동적 블록 평가를 수행하지 않습니다.
`referencedLayerIds`는 실제 화면에 보이는 레이어 목록이 아니며, paper space에서 viewport를 통해 보이는 model 객체도 아직 연결하지 않습니다.
도면 높이·치수를 추론하거나 기본 단위를 mm로 가정하지 않습니다.

객체 ID는 `파일 SHA256:종류:원본 handle`입니다. 동일 파일의 재실행에서는 안정적이나 수정된 파일은 새 revision입니다.
블록 정의와 INSERT 인스턴스를 분리합니다. 블록 안의 선 하나를 현장 부재 하나로 취급하지 마세요.

## 검증

```powershell
dotnet run --project tests/Groundwork.Cad.Tests
```

합성 DWG를 직접 기록하고 다시 읽어 한글, 공간 구분, 블록 참조, ID, JSON, 사용자 규격을 검증합니다.
생성 파일은 `artifacts/tests`에 저장합니다. 이는 실무 도면 정확도 검증을 대신하지 않습니다.
공개 샘플 검증 결과와 향후 구조는 [개발 구조](docs/architecture.md)를 참고하세요.
현장 도면과 생성 결과는 `.gitignore`로 제외합니다.

## 오픈소스

- [ACadSharp](https://github.com/DomCR/ACadSharp), MIT, NuGet 3.8.0 (수정 없이 사용)
- 패키지 버전/해시는 `src/Groundwork.Cad/packages.lock.json`에 고정합니다.
- [ACadSharp 라이선스](https://github.com/DomCR/ACadSharp/blob/master/LICENSE)

개발 대상 브랜치는 `dwg1`입니다. 로컬 수정은 커밋·푸시 후 GitHub에 반영됩니다.
