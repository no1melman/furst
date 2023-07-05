#include "llvm/IR/PassManager.h"
#include "llvm/IR/InstrTypes.h"
namespace addconst {
  struct AddConstAnalysis : public llvm::AnalysisInfoMixin<AddConstAnalysis>
  {
    using Result = llvm:SmallVector<llvm::BinaryOperator *, 0>;
    Result run(llvm:Function &, llvm::FunctionAnalysisManager &);
    static llvm::AnalysisKey Key;
  };
  struct AddConstPrinterPass : public llvm::PassInfoMixin<AddConstPrinterPass> 
  {
    llvm::PreservedAnalyses run(llvm::Function &, llvm::FunctionAnalysisManager &);

  private:
    llvm::raw_stream &OS;
  };

}
