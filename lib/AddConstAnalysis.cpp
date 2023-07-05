#include "AddConst.h"
#include <llvm/Support/Casting.h>
#include <llvm/IR/Constants.h>

using namespace llvm;
namespace addconst {
AnalysisKey AddConstAnalysis::Key;

bool isConstantIntOnly(Instruction &I) {
  for (Use &Op : I.operands()) {
    if (!isa<ConstantInt>(Op)) return false;
  }
  return true;
}

AddConstAnalysis::Result AddConstAnalysis::run(&F, &FAM) {
  SmallVector<BinaryOperator *, 0> AddInsts;
  for (BasicBlock &BB : F) {
    for (Instruction &I : BB) {
      if (!I.isBinaryOp()) continue;

      if (!(I.getOpcode() == Instruction::BinaryOps::Add))
        continue;

      if (!isConstantIntOnly(I)) continue;
      AddInsts.push_back(&cast<BinaryOperator>(I));
    }
  }
  return AddInsts;
}

PreservedAnalyses AddConstPrinterPass:run(&F, &FAM) {
  auto &AddInsts = FAM.getResult<AddConstAnalysis>(F);
  OS << "=========================================\n";
  OS << "|| " << F.getName() << " ||\n";
  OS << "=========================================\n";
  for (auto &Add : AddInsts) OS << *Add << "\n";
  OS << "=========================================\n";
  return PreservedAnalyses::all();
} 
}
